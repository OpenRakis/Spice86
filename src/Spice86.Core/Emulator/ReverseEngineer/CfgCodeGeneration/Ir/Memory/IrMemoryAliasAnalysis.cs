namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Static alias analysis over the <see cref="IrTokens.Memory"/> token ordering of the IR: the
/// pointer reads and writes of one statement are expressed as <see cref="IrMemoryAccess"/>es - a
/// segment part (a segment register and the version the statement reads, or a constant), a
/// normalized 16-bit offset (sorted register/version terms plus a constant displacement, or
/// unknown) and a width. <see cref="Relation"/> proves two accesses equal or disjoint from the SSA
/// versions and the constants <c>IrConstants</c> already substituted; it never reads observed
/// addresses. <see cref="IsKnownRam"/> restricts the rewrites to accesses that provably stay below
/// the device mappings (linear <c>0xA0000</c>); a device read can have a side effect, a store to a
/// device range is ignored. A statement that writes the memory token but is not a plain store or a
/// push is a <see cref="IsFullMemoryBoundary"/>.
/// </summary>
internal sealed class IrMemoryAliasAnalysis {
    /// <summary>The first linear address of a device mapping: everything below is plain RAM.</summary>
    private const uint DeviceBase = 0xA0000;

    private readonly IrSsa _ssa;
    private readonly IrSegmentValueResolver _segmentValues;

    /// <summary>Builds the analysis over the SSA of one method.</summary>
    public IrMemoryAliasAnalysis(IrSsa ssa) {
        _ssa = ssa;
        _segmentValues = new IrSegmentValueResolver(ssa);
    }

    /// <summary>The pointer reads of the statement at <paramref name="index"/> in
    /// <paramref name="block"/>: every pointer occurrence that is not the store target, plus the
    /// <c>SS:[SP]</c> of a pop or a peek.</summary>
    public IReadOnlyList<IrMemoryAccess> Reads(IrBlock block, int index) {
        IrStatement statement = block.Statements[index];
        ValueNode? storeTarget = StoreTarget(statement.Node);
        List<IrMemoryAccess> reads = [];
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(statement.Node)) {
            if (ReferenceEquals(node, storeTarget)) {
                continue;
            }
            switch (node) {
                case SegmentedPointerNode or AbsolutePointerNode:
                    reads.Add(OfPointer((ValueNode)node, statement));
                    break;
                case StackCallValueNode { Operation: StackOperation.Pop or StackOperation.Peek } stack:
                    reads.Add(OfStackAccess(stack.Width, block, index, 0));
                    break;
            }
        }
        return reads;
    }

    /// <summary>The pointer reads of <paramref name="block"/>'s terminator: every pointer
    /// occurrence in its roots - a branch condition, a switch value or an exit transfer - plus
    /// the <c>SS:[SP]</c> of a pop or a peek one embeds.</summary>
    public IReadOnlyList<IrMemoryAccess> Reads(IrBlock block, IrTerminator terminator) {
        List<IrMemoryAccess> reads = [];
        foreach (IVisitableAstNode node in IrAstChildren.TerminatorNodes(terminator)) {
            switch (node) {
                case SegmentedPointerNode or AbsolutePointerNode:
                    reads.Add(OfPointer((ValueNode)node,
                        value => _ssa.VersionUsed(terminator, value)));
                    break;
                case StackCallValueNode { Operation: StackOperation.Pop or StackOperation.Peek }
                        stack:
                    reads.Add(OfStackAccess(stack.Width, block, block.Statements.Count, 0));
                    break;
            }
        }
        return reads;
    }

    /// <summary>The memory writes of the statement at <paramref name="index"/> in
    /// <paramref name="block"/>: the target of a plain store or the <c>SS:[SP - width]</c> of a
    /// push.</summary>
    public IReadOnlyList<IrMemoryAccess> Writes(IrBlock block, int index) {
        IrStatement statement = block.Statements[index];
        if (StoreTarget(statement.Node) is { } target) {
            return [OfPointer(target, statement)];
        }
        if (statement.Node is StackCallNode { Operation: StackOperation.Push } push) {
            return [OfStackAccess(push.Width, block, index, -(int)push.Width / 8)];
        }
        return [];
    }

    /// <summary>The synthetic <c>SS:[SP + displacement]</c> access of <paramref name="width"/>
    /// bytes at the statement <paramref name="index"/> of <paramref name="block"/>: where a stack
    /// slot sitting at <c>SP + displacement</c> lies relative to that statement's SS and SP
    /// versions, for relation checks against other accesses.</summary>
    public IrMemoryAccess StackAccess(IrBlock block, int index, int displacement, int width) =>
        OfStackAccess((BitWidth)(width * 8), block, index, displacement);

    /// <summary>The synthetic <c>SS:[displacement]</c> access of <paramref name="width"/> bytes
    /// with a fully constant offset: a stack slot's access when the current SP is itself a known
    /// constant, so the slot's linear range can be compared against other accesses.</summary>
    public IrMemoryAccess ConstantStackAccess(IrBlock block, int index, int displacement,
        int width) =>
        StackAccess(block, index, displacement & 0xFFFF, width) with { OffsetTerms = [] };

    /// <summary>The 16-bit value <paramref name="statement"/> leaves in SP when it is a plain
    /// <c>SP/ESP = constant</c> assignment, else null.</summary>
    public ushort? StackPointerConstantAfter(IrStatement statement) {
        if (statement.Node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex } left,
                Right: ValueNode right }
            && left is not SegmentRegisterNode && left.DataType.BitWidth != BitWidth.BYTE_8) {
            return IrSegmentValueResolver.ConstantWordOf(right);
        }
        return null;
    }

    /// <summary>Whether the statement at <paramref name="index"/> writes the memory token without
    /// being exactly a plain store whose stored value and target address parts write no
    /// <see cref="IrTokens.Memory"/> token, or exactly a push whose argument writes none - a call,
    /// an interrupt, a callback, an event check, an opaque statement, a store whose value or
    /// address itself writes memory, or a helper the lifter did not split: a boundary no access
    /// may cross.</summary>
    public bool IsFullMemoryBoundary(IrBlock block, int index) {
        IrStatement statement = block.Statements[index];
        if ((statement.Effects.TokenWrites & IrTokens.Memory) == IrTokens.None) {
            return false;
        }
        return statement.Node switch {
            BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: SegmentedPointerNode or AbsolutePointerNode } assign
                => WritesMemoryToken(assign.Right) || WritesMemoryToken(assign.Left),
            StackCallNode { Operation: StackOperation.Push } push
                => push.Arguments.OfType<ValueNode>().Any(WritesMemoryToken),
            _ => true
        };
    }

    private static bool WritesMemoryToken(ValueNode node) =>
        (IrEffectsVisitor.Of(node, false).TokenWrites & IrTokens.Memory) != IrTokens.None;

    /// <summary>How <paramref name="first"/> and <paramref name="second"/> relate. Same segment
    /// base - same segment register version, or the same known segment value - compares the
    /// 16-bit circular offset ranges when the offset terms are the same. Any other case compares
    /// the linear byte ranges when both are fully constant, each taken with and without the A20
    /// wrap (<c>&amp; 0xFFFFF</c>): <see cref="IrAliasRelation.Equal"/> only when they are the same
    /// range both ways, <see cref="IrAliasRelation.Disjoint"/> only when no pair intersects. A
    /// range crossing the 1 MiB A20 boundary is <see cref="IrAliasRelation.MayAlias"/> unless the
    /// two ranges are identical: masking its endpoints independently does not describe where its
    /// bytes actually land.</summary>
    public IrAliasRelation Relation(IrMemoryAccess first, IrMemoryAccess second) {
        if (SameSegmentBase(first, second)) {
            return OffsetRelation(first, second);
        }
        (uint Start, uint End)? firstRange = LinearRange(first);
        (uint Start, uint End)? secondRange = LinearRange(second);
        if (firstRange is null || secondRange is null) {
            return IrAliasRelation.MayAlias;
        }
        if (firstRange == secondRange && Wrapped(firstRange.Value) == Wrapped(secondRange.Value)) {
            return IrAliasRelation.Equal;
        }
        if ((firstRange.Value.Start >> 20) != (firstRange.Value.End >> 20)
            || (secondRange.Value.Start >> 20) != (secondRange.Value.End >> 20)) {
            return IrAliasRelation.MayAlias;
        }
        foreach ((uint start, uint end) in new[] { firstRange.Value, Wrapped(firstRange.Value) }) {
            foreach ((uint otherStart, uint otherEnd) in
                new[] { secondRange.Value, Wrapped(secondRange.Value) }) {
                if (RangesIntersect(start, end, otherStart, otherEnd)) {
                    return IrAliasRelation.MayAlias;
                }
            }
        }
        return IrAliasRelation.Disjoint;
    }

    /// <summary>Whether every byte the access can reach lies below the device mappings at
    /// <c>0xA0000</c>: the segment value is known and the whole reachable range - the constant
    /// offset range, or the whole segment window when the offset is not constant - stays under it.
    /// An unknown segment value is never known RAM.</summary>
    public bool IsKnownRam(IrMemoryAccess access) {
        if (access.LinearAddress is { } address) {
            return address + (uint)access.Width <= DeviceBase;
        }
        if (access.SegmentValue is not { } segment) {
            return false;
        }
        uint segmentBase = (uint)segment * 16;
        if (access.OffsetTerms is { Count: 0 }) {
            return segmentBase + (uint)(ushort)access.OffsetDisplacement + (uint)access.Width
                <= DeviceBase;
        }
        return segmentBase + 0xFFFF + (uint)access.Width <= DeviceBase;
    }

    /// <summary>The access of one pointer <paramref name="node"/> read or written by
    /// <paramref name="statement"/>.</summary>
    internal IrMemoryAccess OfPointer(ValueNode node, IrStatement statement) =>
        OfPointer(node, value => _ssa.VersionUsed(statement, value));

    private IrMemoryAccess OfPointer(ValueNode node, Func<IrSsaValue, int> versionUsed) {
        switch (node) {
            case SegmentedPointerNode pointer: {
                (IrSsaValue? segmentRegister, int segmentVersion, ushort? segmentValue) =
                    SegmentOf(pointer.Segment, versionUsed);
                (IReadOnlyList<OffsetTerm>? terms, int displacement) =
                    OffsetOf(pointer.Offset, versionUsed);
                return new IrMemoryAccess {
                    SegmentRegister = segmentRegister, SegmentVersion = segmentVersion,
                    SegmentValue = segmentValue, OffsetTerms = terms,
                    OffsetDisplacement = displacement, Width = (int)pointer.DataType.BitWidth / 8
                };
            }
            case AbsolutePointerNode pointer: {
                IrMemoryAccess access = new() {
                    OffsetTerms = [], Width = (int)pointer.DataType.BitWidth / 8
                };
                if (pointer.AbsoluteAddress is ConstantNode address) {
                    access = access with { LinearAddress = (uint)address.Value };
                }
                return access;
            }
            default:
                throw new ArgumentException($"{node.GetType()} is not a memory pointer.", nameof(node));
        }
    }

    /// <summary>The <c>SS:[SP + displacement]</c> access of a stack operation of
    /// <paramref name="width"/> at the statement <paramref name="index"/> of
    /// <paramref name="block"/>: the SS and SP versions live before it.</summary>
    private IrMemoryAccess OfStackAccess(BitWidth width, IrBlock block, int index,
        int displacement) {
        int segmentVersion = _ssa.VersionBefore(block, index, IrSsaValue.Ss);
        return new IrMemoryAccess {
            SegmentRegister = IrSsaValue.Ss, SegmentVersion = segmentVersion,
            SegmentValue = _segmentValues.KnownSegmentValue(segmentVersion),
            OffsetTerms = [new OffsetTerm(new RegisterNode(DataType.UINT16,
                (int)RegisterIndex.SpIndex), _ssa.VersionBefore(block, index, IrSsaValue.Esp))],
            OffsetDisplacement = displacement, Width = (int)width / 8
        };
    }

    /// <summary>The segment part of a pointer's <paramref name="segment"/> node: a segment register
    /// and the version the statement reads, a constant, or nothing known.</summary>
    private (IrSsaValue? SegmentRegister, int Version, ushort? Value) SegmentOf(ValueNode segment,
        Func<IrSsaValue, int> versionUsed) {
        switch (segment) {
            case SegmentRegisterNode register: {
                IrSsaValue value = (IrSsaValue)((int)IrSsaValue.Es + register.RegisterIndex);
                int version = versionUsed(value);
                return (value, version, _segmentValues.KnownSegmentValue(version));
            }
            case ConstantNode constant:
                return (null, -1, (ushort)constant.Value);
            default:
                return (null, -1, null);
        }
    }

    /// <summary>The normalized offset of <paramref name="node"/>: the sorted (register, version)
    /// terms and the constant displacement, or an unknown offset (<c>null</c> terms). Only 16-bit
    /// offsets are analysed.</summary>
    private (IReadOnlyList<OffsetTerm>? Terms, int Displacement) OffsetOf(ValueNode node,
        Func<IrSsaValue, int> versionUsed) {
        if (node.DataType.BitWidth != BitWidth.WORD_16
            || Normalize(node, versionUsed) is not { } normalized) {
            return (null, 0);
        }
        (List<OffsetTerm> terms, int displacement) = normalized;
        terms.Sort((first, second) => {
            if (first.Register.RegisterIndex != second.Register.RegisterIndex) {
                return first.Register.RegisterIndex.CompareTo(second.Register.RegisterIndex);
            }
            return first.Version.CompareTo(second.Version);
        });
        return (terms, displacement & 0xFFFF);
    }

    /// <summary>The terms and displacement an offset node normalizes to: a constant, a 16-bit
    /// register, a conversion to a 16-bit type around an accepted form, or a + / - of accepted
    /// forms where the subtracted side is a constant.</summary>
    private (List<OffsetTerm> Terms, int Displacement)? Normalize(ValueNode node,
        Func<IrSsaValue, int> versionUsed) {
        switch (node) {
            case ConstantNode constant:
                return ([], (int)constant.Value & 0xFFFF);
            case RegisterNode register when register is not SegmentRegisterNode
                    && register.DataType.BitWidth == BitWidth.WORD_16:
                return ([new OffsetTerm(register,
                    versionUsed((IrSsaValue)register.RegisterIndex))], 0);
            case TypeConversionNode { DataType.BitWidth: BitWidth.WORD_16 } conversion:
                return Normalize(conversion.Value, versionUsed);
            case BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS,
                Left: ValueNode left, Right: ValueNode right }: {
                (List<OffsetTerm>, int)? leftParts = Normalize(left, versionUsed);
                (List<OffsetTerm>, int)? rightParts = Normalize(right, versionUsed);
                if (leftParts is null || rightParts is null) {
                    return null;
                }
                return (([.. leftParts.Value.Item1, .. rightParts.Value.Item1],
                    leftParts.Value.Item2 + rightParts.Value.Item2));
            }
            case BinaryOperationNode { BinaryOperation: BinaryOperation.MINUS,
                Left: ValueNode left, Right: ConstantNode constant }: {
                (List<OffsetTerm>, int)? leftParts = Normalize(left, versionUsed);
                if (leftParts is null) {
                    return null;
                }
                return (leftParts.Value.Item1,
                    leftParts.Value.Item2 - ((int)constant.Value & 0xFFFF));
            }
            case InstructionFieldNode { ResolvedNode: ValueNode resolved }
                when resolved is ConstantNode:
                return Normalize(resolved, versionUsed);
            default:
                return null;
        }
    }

    private static ValueNode? StoreTarget(IVisitableAstNode node) {
        if (node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: SegmentedPointerNode or AbsolutePointerNode } assign) {
            return assign.Left;
        }
        return null;
    }

    /// <summary>Same base to compare offsets in: the same segment register version, or the same
    /// known segment value.</summary>
    private static bool SameSegmentBase(IrMemoryAccess first, IrMemoryAccess second) =>
        (first.SegmentRegister is { } firstRegister && second.SegmentRegister is { } secondRegister
            && firstRegister == secondRegister && first.SegmentVersion == second.SegmentVersion)
        || (first.SegmentValue is { } firstValue && second.SegmentValue is { } secondValue
            && firstValue == secondValue);

    /// <summary>Equal or disjoint 16-bit circular offset ranges of two same-base accesses, when
    /// their offset terms are the same.</summary>
    private static IrAliasRelation OffsetRelation(IrMemoryAccess first, IrMemoryAccess second) {
        if (first.OffsetTerms is null || second.OffsetTerms is null
            || !first.OffsetTerms.SequenceEqual(second.OffsetTerms)) {
            return IrAliasRelation.MayAlias;
        }
        int firstStart = first.OffsetDisplacement & 0xFFFF;
        int secondStart = second.OffsetDisplacement & 0xFFFF;
        if (firstStart == secondStart && first.Width == second.Width) {
            return IrAliasRelation.Equal;
        }
        if (CircularDisjoint(firstStart, first.Width, secondStart, second.Width)) {
            return IrAliasRelation.Disjoint;
        }
        return IrAliasRelation.MayAlias;
    }

    /// <summary>Whether the circular byte ranges <c>[start1, start1 + width1)</c> and
    /// <c>[start2, start2 + width2)</c> cannot intersect on the 16-bit offset ring.</summary>
    private static bool CircularDisjoint(int start1, int width1, int start2, int width2) {
        int distance = (start2 - start1) & 0xFFFF;
        return distance >= width1 && (0x10000 - distance) >= width2;
    }

    /// <summary>The linear byte range of an access, when its segment value is known and its offset
    /// is fully constant - or it is an absolute pointer of constant address.</summary>
    private static (uint Start, uint End)? LinearRange(IrMemoryAccess access) {
        if (access.LinearAddress is { } address) {
            return (address, address + (uint)access.Width - 1);
        }
        if (access.SegmentValue is { } segment && access.OffsetTerms is { Count: 0 }) {
            uint start = (uint)segment * 16 + (uint)(ushort)access.OffsetDisplacement;
            return (start, start + (uint)access.Width - 1);
        }
        return null;
    }

    private static (uint Start, uint End) Wrapped((uint Start, uint End) range) =>
        (range.Start & 0xFFFFF, range.End & 0xFFFFF);

    private static bool RangesIntersect(uint start1, uint end1, uint start2, uint end2) =>
        start1 <= end2 && start2 <= end1;
}
