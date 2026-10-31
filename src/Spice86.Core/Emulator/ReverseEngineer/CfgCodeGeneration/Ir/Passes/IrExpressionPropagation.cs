namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// A register version produced by a plain assignment and
/// read exactly once is merged into its reader, so one statement covers several instructions:
/// <c>add bx,1</c> followed by <c>shr bx,1</c> emits <c>BX = (ushort)((ushort)(BX + 1) &gt;&gt; 1);</c>.
/// Legality is the one rule <see cref="IrSsa.SameVersions"/> over the moved expression's reads and token
/// reads - the memory token covers stores and pushes, the IO token port accesses - plus no faulting
/// or guard statement between the writer and the reader. An event check needs no refusal of its own:
/// its may-write re-versions the registers a handler can observe, so the version rule already blocks
/// a merge across it.
/// </summary>
internal static class IrExpressionPropagation {
    /// <summary>The AST-node budget of one merged expression.</summary>
    private const int MaxMergedNodes = 16;
    /// <summary>How many instructions one statement may cover.</summary>
    private const int MaxChainLength = 3;

    /// <summary>The <see cref="IrSsaValue"/> bit set of the general and segment registers.</summary>
    private const uint RegisterValues = (1u << (int)IrSsaValue.Gs + 1) - 1;

    /// <summary>Merges the single-use register writes of <paramref name="method"/> into their readers.</summary>
    public static void Apply(IrMethod method, IrSsa ssa) {
        HashSet<ICfgNode> entryNodes = method.Entries.Select(entry => entry.Node).ToHashSet();
        foreach (IrBlock block in method.Blocks) {
            MergeBlock(block, entryNodes, ssa);
        }
    }

    private static void MergeBlock(IrBlock block, HashSet<ICfgNode> entryNodes, IrSsa ssa) {
        int[] covered = new int[block.Statements.Count];
        Array.Fill(covered, 1);
        List<int> removed = [];
        for (int i = 0; i < block.Statements.Count; i++) {
            if (covered[i] > 0
                && TryMerge(block, i, entryNodes, ssa, covered) is not null) {
                removed.Add(i);
            }
        }
        foreach (int index in removed.OrderByDescending(index => index)) {
            ssa.Remove(block.Statements[index]);
            block.Statements.RemoveAt(index);
        }
    }

    /// <summary>Merges the assignment of statement <paramref name="i"/> into the only statement reading
    /// its register when every condition holds; returns the reader's index.</summary>
    private static int? TryMerge(IrBlock block, int i, HashSet<ICfgNode> entryNodes, IrSsa ssa,
        int[] covered) {
        IrStatement writer = block.Statements[i];
        if (entryNodes.Contains(writer.Origin)
            || writer.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode r1,
                Right: ValueNode e1
            }
            || r1 is SegmentRegisterNode
            || (IrValueSets.Of(r1) & IrValues.Sp) != IrValues.None
            || !Mergeable(e1)) {
            return null;
        }
        uint r1Bits = IrSsaValues.ValueBits(IrValueSets.Of(r1));
        if (r1Bits == 0 || (r1Bits & (r1Bits - 1)) != 0) {
            return null;
        }
        IrSsaValue r1Value = (IrSsaValue)System.Numerics.BitOperations.TrailingZeroCount(r1Bits);
        int writerVersion = -1;
        uint definedRegisters = 0;
        foreach ((IrSsaValue value, int version) in ssa.Definitions(writer)) {
            if (value == r1Value) {
                writerVersion = version;
            } else if ((RegisterValues & (1u << (int)value)) != 0) {
                definedRegisters |= 1u << (int)value;
            } else if (value < IrSsaValue.Memory && ssa.IsLive(version)) {
                return null;
            } else if (value >= IrSsaValue.Memory) {
                return null;
            }
        }
        if (writerVersion < 0 || definedRegisters != 0) {
            return null;
        }
        if (ssa.LiveUses(writerVersion) is not [IrSsaUse { Phi: null, Statement: not null }]) {
            return null;
        }
        int j = ReaderIndex(block, r1Value, writerVersion, i, ssa);
        if (j < 0) {
            return null;
        }
        IrStatement reader = block.Statements[j];
        if (entryNodes.Contains(reader.Origin)) {
            return null;
        }
        for (int k = i; k <= j; k++) {
            IrStatement between = block.Statements[k];
            if (between.Effects.MayFault
                || between.Node is SpeculativeGuardNode) {
                return null;
            }
        }
        if (reader.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: ValueNode r2,
                Right: ValueNode e2
            } assign
            || !Mergeable(e2)) {
            return null;
        }
        List<RegisterNode> expressionRegisters = [];
        CollectRegisters(e2, expressionRegisters);
        if (expressionRegisters.Count(register => register.Equals(r1)) != 1
            || expressionRegisters.Any(register => !register.Equals(r1) && Overlaps(register, r1))) {
            return null;
        }
        List<RegisterNode> addressRegisters = [];
        CollectAddressRegisters(r2, addressRegisters);
        if (addressRegisters.Any(register => Overlaps(register, r1)) || !MergeableTarget(r2)) {
            return null;
        }
        IrEffects moved = IrEffectsVisitor.Of(e1, false);
        IrTokens tokens = moved.TokenReads;
        if (moved.TouchesMemory) {
            tokens |= IrTokens.Io;
        }
        // Compare "after the writer" (before the next statement) with "before the reader": the merged
        // register itself legitimately changes at the writer - that is the merge. Anything else
        // redefined in between refuses.
        if (!ssa.SameVersions(moved.Reads, tokens, (block, i + 1), (block, j))) {
            return null;
        }
        ValueNode merged = Substitute(e2, r1, e1);
        if (CountNodes(merged) > MaxMergedNodes || covered[i] + covered[j] > MaxChainLength) {
            return null;
        }
        IrStatement replacement = IrStatement.Create(assign with { Right = merged }, reader.Origin);
        ssa.Replace(reader, replacement);
        block.Statements[j] = replacement;
        covered[j] += covered[i];
        covered[i] = 0;
        return j;
    }

    /// <summary>The index of the one statement of <paramref name="block"/> using
    /// <paramref name="version"/>, located through the site table so edits of earlier passes cannot
    /// stale it; -1 when it is not exactly one statement after <paramref name="writerIndex"/>.</summary>
    private static int ReaderIndex(IrBlock block, IrSsaValue value, int version, int writerIndex,
        IrSsa ssa) {
        int found = -1;
        for (int index = writerIndex + 1; index < block.Statements.Count; index++) {
            if (ssa.VersionUsed(block.Statements[index], value) != version) {
                continue;
            }
            if (found >= 0) {
                return -1;
            }
            found = index;
        }
        return found;
    }

    private static bool Overlaps(RegisterNode register, RegisterNode other) =>
        (IrValueSets.Of(register) & IrValueSets.Of(other)) != IrValues.None;

    /// <summary>Whether <paramref name="node"/> can travel into another statement: a pure read of
    /// registers, constants, conversions, unary and non-assignment binary operations, or a memory
    /// read whose address parts are all mergeable.</summary>
    private static bool Mergeable(ValueNode node) =>
        IrAstChildren.Descendants(node).All(descendant => descendant switch {
            RegisterNode or ConstantNode or InstructionFieldNode or TypeConversionNode
                or UnaryOperationNode or ConditionalValueNode or SegmentedPointerNode
                or AbsolutePointerNode => true,
            ExplicitCastNode cast => cast.Value is ValueNode,
            BinaryOperationNode binary => binary.BinaryOperation != BinaryOperation.ASSIGN,
            _ => false
        });

    /// <summary>A merge target: a register, a segment register or a memory pointer whose address is mergeable.</summary>
    private static bool MergeableTarget(ValueNode node) => node switch {
        RegisterNode => true,
        SegmentedPointerNode pointer => Mergeable(pointer.Segment) && Mergeable(pointer.Offset)
            && (pointer.DefaultSegment is null || Mergeable(pointer.DefaultSegment)),
        AbsolutePointerNode pointer => Mergeable(pointer.AbsoluteAddress),
        _ => false
    };

    /// <summary>The registers a write target reads: only the address parts of a memory pointer; a
    /// register target is written, not read.</summary>
    private static void CollectAddressRegisters(ValueNode target, List<RegisterNode> registers) {
        switch (target) {
            case SegmentedPointerNode pointer:
                CollectRegisters(pointer.Segment, registers);
                if (pointer.DefaultSegment is not null) {
                    CollectRegisters(pointer.DefaultSegment, registers);
                }
                CollectRegisters(pointer.Offset, registers);
                return;
            case AbsolutePointerNode pointer:
                CollectRegisters(pointer.AbsoluteAddress, registers);
                return;
        }
    }

    private static void CollectRegisters(ValueNode node, List<RegisterNode> registers) =>
        registers.AddRange(IrAstChildren.Descendants(node).OfType<RegisterNode>()
            .Where(register => register is not SegmentRegisterNode));

    private static ValueNode Substitute(ValueNode node, RegisterNode target, ValueNode replacement) =>
        node switch {
            RegisterNode register when register.Equals(target) => replacement,
            _ => (ValueNode)IrAstChildren.MapChildren(node,
                child => child is ValueNode value ? Substitute(value, target, replacement) : child)
        };

    private static int CountNodes(ValueNode node) {
        return node switch {
            BinaryOperationNode binary => 1 + CountNodes(binary.Left) + CountNodes(binary.Right),
            UnaryOperationNode unary => 1 + CountNodes(unary.Value),
            TypeConversionNode conversion => 1 + CountNodes(conversion.Value),
            ExplicitCastNode { Value: ValueNode inner } => 1 + CountNodes(inner),
            ConditionalValueNode conditional => 1 + CountNodes(conditional.Condition)
                + CountNodes(conditional.WhenTrue) + CountNodes(conditional.WhenFalse),
            SegmentedPointerNode pointer => 1 + CountNodes(pointer.Segment) + CountNodes(pointer.Offset)
                + (pointer.DefaultSegment is null ? 0 : CountNodes(pointer.DefaultSegment)),
            AbsolutePointerNode pointer => 1 + CountNodes(pointer.AbsoluteAddress),
            MethodCallValueNode call => 1 + call.CallNode.Arguments
                .Sum(argument => argument is ValueNode value ? CountNodes(value) : 1),
            _ => 1
        };
    }
}
