namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The frame analysis: which stack accesses of a method sit at a known offset from the SP the
/// method was entered with (<see cref="Accesses"/>), and whether an entry holds the standard
/// <c>push bp; mov bp,sp; [sub sp,n]</c> prologue with its epilogues (<see cref="FrameAt"/>).
/// <para>A 16-bit general-register value (the low word of EAX ... EDI, except ESP) at an SSA
/// version is an <b>SP copy at entry delta d</b> when:</para>
/// <list type="bullet">
/// <item>its defining statement is <c>R = SP</c> (16-bit registers, nothing else on the right) and
/// the <see cref="IrStackSlots"/> offset before it resolves to the entry at delta d (an offset
/// rebased on a statement that does not write SP or SS is followed back to the entry; any SP / SS
/// write ends the chain);</item>
/// <item>its defining statement is <c>R = R2</c> (16-bit plain copy) and the version of R2 it reads
/// is an SP copy at d;</item>
/// <item>it is a phi whose operands are all SP copies at the same d (a version still being decided
/// counts as not a copy, which settles phi cycles; an entry operand makes the phi not a copy);</item>
/// <item>it is a partial definition by a call / <c>int</c> statement whose
/// <see cref="HardwareInterruptEffectBuilder.CallTargetWrites"/> does not include the register's
/// low word and whose previous version is an SP copy at d.</item>
/// </list>
/// <para>Anything else is not a copy: an entry version, a load, arithmetic, a byte write, an event
/// check that may write it, unknown code. A memory access whose segment is SS and whose offset is
/// exactly one 16-bit register term at an SP-copy version is recorded with
/// <c>EntryOffset = d + displacement</c>: the return address is at <c>[0, ReturnAddressBytes)</c>,
/// the arguments start at <c>ReturnAddressBytes</c>, the saved BP of a standard frame is at -2 and
/// the locals are below. Accesses through DS are not recorded, even when DS equals SS.</para>
/// </summary>
internal sealed class IrFrames {
    private static readonly CpuValues FrameValues = CpuValues.Esp | CpuValues.Ebp;
    private static readonly CpuValues StackValues = CpuValues.Esp | CpuValues.Ss;

    private readonly IrSsa _ssa;
    private readonly IrStackSlots _slots;
    private readonly IrMemoryAliasAnalysis _alias;
    private readonly Func<IrStatement, CpuValues> _callWrites;
    private readonly Dictionary<int, IrSsaPhi> _phiByResult;
    private readonly Dictionary<int, int?> _copyDelta = [];
    private readonly HashSet<int> _deciding = [];
    private readonly List<IrFrameAccess> _accesses = [];
    private readonly Dictionary<IrStatement, List<IrFrameAccess>> _accessesByStatement =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<(SegmentedPointerNode Pointer, IrFrameAccess? Access)> _pointerResults = [];
    private readonly Dictionary<IrEntry, IrFrame?> _frames = [];

    /// <summary>Runs the analysis on <paramref name="method"/>. <paramref name="callWrites"/>
    /// returns the registers a call / <c>int</c> statement may write
    /// (<see cref="CpuValues.Registers"/> when unknown).</summary>
    internal static IrFrames Compute(IrMethod method, IrSsa ssa, IrStackSlots slots,
        Func<IrStatement, CpuValues> callWrites) {
        IrFrames frames = new(ssa, slots, callWrites);
        frames.CollectAccesses(method);
        frames.CollectFrames(method);
        return frames;
    }

    private IrFrames(IrSsa ssa, IrStackSlots slots, Func<IrStatement, CpuValues> callWrites) {
        _ssa = ssa;
        _slots = slots;
        _alias = new IrMemoryAliasAnalysis(ssa);
        _callWrites = callWrites;
        _phiByResult = ssa.Phis.ToDictionary(phi => phi.Result);
    }

    /// <summary>Every recognized stack access, in method order.</summary>
    internal IReadOnlyList<IrFrameAccess> Accesses => _accesses;

    /// <summary>The recognized accesses of <paramref name="statement"/>.</summary>
    internal IReadOnlyList<IrFrameAccess> AccessesOf(IrStatement statement) {
        if (_accessesByStatement.TryGetValue(statement, out List<IrFrameAccess>? accesses)) {
            return accesses;
        }
        return [];
    }

    /// <summary>One entry per <see cref="SegmentedPointerNode"/> occurrence of the method's
    /// statements, in statement order: the frame access the occurrence was proved to be, or null.
    /// Occurrences are the shared node objects the printed AST carries, so a node with an entry
    /// that is null or names a different slot keeps the indexer.</summary>
    internal IReadOnlyList<(SegmentedPointerNode Pointer, IrFrameAccess? Access)> PointerResults =>
        _pointerResults;

    /// <summary>2, 4 or 6 when the method's return exits are uniformly a near <c>ret</c>, a
    /// <c>retf</c> or an <c>iret</c>; null when they disagree or there is none.</summary>
    internal int? ReturnAddressBytes { get; private set; }

    /// <summary>The standard frame at <paramref name="entry"/>, null when the entry block does not
    /// open with the <c>push bp; mov bp,sp</c> prologue.</summary>
    internal IrFrame? FrameAt(IrEntry entry) =>
        _frames.TryGetValue(entry, out IrFrame? frame) ? frame : null;

    private void CollectAccesses(IrMethod method) {
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                CollectAccesses(statement);
            }
        }
    }

    /// <summary>Every pointer occurrence of <paramref name="statement"/> is evaluated once (the
    /// store target of an <c>ASSIGN</c> is the write, every other occurrence a read), so a shared
    /// node is judged occurrence by occurrence.</summary>
    private void CollectAccesses(IrStatement statement) {
        ValueNode? storeTarget = null;
        if (statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: SegmentedPointerNode or AbsolutePointerNode } assign) {
            storeTarget = assign.Left;
        }
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(statement.Node)) {
            if (node is not SegmentedPointerNode pointer) {
                continue;
            }
            IrFrameAccess? access = FrameAccessOf(statement, pointer,
                ReferenceEquals(pointer, storeTarget));
            _pointerResults.Add((pointer, access));
            if (access is null) {
                continue;
            }
            _accesses.Add(access);
            if (!_accessesByStatement.TryGetValue(statement, out List<IrFrameAccess>? list)) {
                list = [];
                _accessesByStatement[statement] = list;
            }
            list.Add(access);
        }
    }

    /// <summary>The frame access <paramref name="pointer"/> performs at
    /// <paramref name="statement"/>, or null when the frame rule does not prove it: the access must
    /// be through SS with exactly one 16-bit register term (not SP) whose version is an SP copy at
    /// a known entry delta.</summary>
    private IrFrameAccess? FrameAccessOf(IrStatement statement, SegmentedPointerNode pointer,
        bool isWrite) {
        IrMemoryAccess access = _alias.OfPointer(pointer, statement);
        if (access.SegmentRegister != IrSsaValue.Ss || access.OffsetTerms is not [OffsetTerm term]
            || term.Register.DataType.BitWidth != BitWidth.WORD_16
            || term.Register.RegisterIndex == (int)RegisterIndex.SpIndex) {
            return null;
        }
        IrSsaValue value = (IrSsaValue)term.Register.RegisterIndex;
        if (SpCopyDelta(value, term.Version) is not { } delta) {
            return null;
        }
        return new IrFrameAccess(statement, (RegisterIndex)term.Register.RegisterIndex,
            delta + (short)access.OffsetDisplacement, access.Width, isWrite, pointer);
    }

    /// <summary>The entry delta the version is an SP copy at, or null when it is not an SP copy.
    /// Memoized; a version still being decided reports "not a copy", which settles phi cycles.</summary>
    private int? SpCopyDelta(IrSsaValue value, int version) {
        if (version < 0 || value is < IrSsaValue.Eax or > IrSsaValue.Edi
            || value == IrSsaValue.Esp) {
            return null;
        }
        if (_copyDelta.TryGetValue(version, out int? cached)) {
            return cached;
        }
        if (!_deciding.Add(version)) {
            return null;
        }
        int? result = ComputeCopyDelta(value, version);
        _deciding.Remove(version);
        _copyDelta[version] = result;
        return result;
    }

    private int? ComputeCopyDelta(IrSsaValue value, int version) {
        IrSsaVersionInfo info = _ssa.Info(version);
        switch (info.Kind) {
            case IrSsaDefinitionKind.Phi:
                return PhiCopyDelta(value, version);
            case IrSsaDefinitionKind.Definition:
                return CopyAssignDelta(value, _ssa.DefiningStatement(version));
            case IrSsaDefinitionKind.PartialDefinition:
                return CopyAssignDelta(value, _ssa.DefiningStatement(version))
                       ?? PartialCopyDelta(value, version, info);
            default:
                return null;
        }
    }

    private int? PhiCopyDelta(IrSsaValue value, int version) {
        if (!_phiByResult.TryGetValue(version, out IrSsaPhi? phi) || phi.HasEntryOperand) {
            return null;
        }
        int? delta = null;
        foreach ((IrBlock _, int operandVersion) in phi.Operands) {
            int? operandDelta = SpCopyDelta(value, operandVersion);
            if (operandDelta is null || (delta is not null && operandDelta != delta)) {
                return null;
            }
            delta = operandDelta;
        }
        return delta;
    }

    /// <summary>A partial definition keeps the SP copy when a call / <c>int</c> makes it and the
    /// observed targets cannot write the register's low word.</summary>
    private int? PartialCopyDelta(IrSsaValue value, int version, IrSsaVersionInfo info) {
        IrStatement? statement = _ssa.DefiningStatement(version);
        if (statement is null || info.Previous < 0
            || statement.Node is CheckExternalEventsNode or SpeculativeGuardNode
            || statement.Origin is not CfgInstruction instruction
            || (!instruction.IsCall && !IsInterruptCall(instruction))) {
            return null;
        }
        CpuValues lowWord = IrSsaValues.LowWordMaskOf(value);
        if ((_callWrites(statement) & lowWord) != CpuValues.None) {
            return null;
        }
        return SpCopyDelta(value, info.Previous);
    }

    /// <summary>The copy rules on the defining statement itself: <c>R = SP</c> takes the entry
    /// delta of the point it runs at; <c>R = R2</c> forwards the copy of R2. A 16-bit register
    /// write is a partial definition of its 32-bit value, so both definition kinds come here.</summary>
    private int? CopyAssignDelta(IrSsaValue value, IrStatement? statement) {
        if (statement?.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } left,
                Right: RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } right }
            || left.RegisterIndex != (int)value) {
            return null;
        }
        if (right.RegisterIndex == (int)RegisterIndex.SpIndex) {
            return EntryDelta(_slots.OffsetBefore(statement));
        }
        if (right.RegisterIndex is < (int)RegisterIndex.AxIndex or > (int)RegisterIndex.DiIndex) {
            return null;
        }
        IrSsaValue rightValue = (IrSsaValue)right.RegisterIndex;
        return SpCopyDelta(rightValue, _ssa.VersionUsed(statement, rightValue));
    }

    private void CollectFrames(IrMethod method) {
        List<IrFrameEpilogue> epilogues = EpiloguesOf(method);
        ReturnAddressBytes = UniformReturnAddressBytes(epilogues);
        foreach (IrEntry entry in method.Entries) {
            _frames[entry] = DetectFrame(entry, epilogues);
        }
    }

    private IrFrame? DetectFrame(IrEntry entry, List<IrFrameEpilogue> epilogues) {
        List<IrStatement> statements = entry.Block.Statements;
        int pushIndex = statements.FindIndex(IsPushBp);
        if (pushIndex < 0 || !EntryDelta(statements[pushIndex], 0)) {
            return null;
        }
        IrStatement? setBp = null;
        int setBpIndex = -1;
        for (int index = pushIndex + 1; index < statements.Count; index++) {
            IrStatement statement = statements[index];
            if (!WritesFrameRegister(statement)) {
                continue;
            }
            if (IsSetBp(statement) && EntryDelta(statement, -2)) {
                setBp = statement;
                setBpIndex = index;
            }
            break;
        }
        if (setBp is null) {
            return null;
        }
        return new IrFrame(entry, statements[pushIndex], setBp,
            LocalBytes(statements, setBpIndex), epilogues,
            UniformReturnAddressBytes(epilogues), PoppedArgumentBytes(epilogues));
    }

    private bool EntryDelta(IrStatement statement, int delta) =>
        EntryDelta(_slots.OffsetBefore(statement)) == delta;

    /// <summary>The offset resolved back to the method entry: an offset the analysis rebased on a
    /// statement carries that statement as <see cref="IrStackOffset.Base"/> and its delta is
    /// relative to the SP the base saw. The chain is followed only through a base statement that
    /// writes neither SP / ESP nor SS (an event check, a memory-boundary store): SP after such a
    /// statement is SP before it. Any SP / SS write ends the chain - a rebase on it left the
    /// entry-relative SP unknown.</summary>
    internal int? EntryDelta(IrStackOffset? offset) {
        if (offset is not { Kind: IrStackOffsetKind.Known } current) {
            return null;
        }
        int delta = current.Delta;
        HashSet<IrStatement> seen = [];
        for (IrStatement? base_ = current.Base; base_ is not null && seen.Add(base_);
             base_ = current.Base) {
            if (((base_.Effects.MustWrite | base_.Effects.MayWrite) & StackValues)
                != CpuValues.None
                || _slots.OffsetBefore(base_) is not { Kind: IrStackOffsetKind.Known } before) {
                return null;
            }
            current = before;
            delta += before.Delta;
        }
        return delta;
    }

    private int LocalBytes(List<IrStatement> statements, int setBpIndex) {
        for (int index = setBpIndex + 1; index < statements.Count; index++) {
            IrStatement statement = statements[index];
            if (!TouchesFrameRegister(statement)) {
                continue;
            }
            if (EntryDelta(_slots.OffsetBefore(statement)) is { } before
                && EntryDelta(_slots.OffsetAfter(statement)) is { } after && after < before) {
                return before - after;
            }
            return 0;
        }
        return 0;
    }

    private List<IrFrameEpilogue> EpiloguesOf(IrMethod method) =>
        [.. method.Blocks
            .Where(block => block.Terminator is IrExit { Origin: CfgInstruction instruction }
                && instruction.Kind.HasFlag(InstructionKind.Return))
            .Select(block => new IrFrameEpilogue((IrExit)block.Terminator, EpilogueKind(block)))];

    private IrFrameEpilogueKind EpilogueKind(IrBlock block) {
        List<IrStatement> stackStatements = [.. block.Statements.Where(TouchesFrameRegister)];
        if (stackStatements.Count == 0) {
            return IrFrameEpilogueKind.Other;
        }
        IrStatement last = stackStatements[^1];
        if (last.Node is StackCallNode { Operation: StackOperation.Leave }) {
            return IrFrameEpilogueKind.Leave;
        }
        if (!IsPopBp(last)) {
            return IrFrameEpilogueKind.Other;
        }
        if (stackStatements.Count >= 2 && IsSetBpFromCopy(stackStatements[^2])) {
            return IrFrameEpilogueKind.MoveSpPopBp;
        }
        return EntryDelta(last, -2) ? IrFrameEpilogueKind.PopBp : IrFrameEpilogueKind.Other;
    }

    /// <summary>2 / 4 / 6 when every return exit is uniformly a near <c>ret</c>, a <c>retf</c> or an
    /// <c>iret</c>; null when they disagree or there is none.</summary>
    private static int? UniformReturnAddressBytes(List<IrFrameEpilogue> epilogues) {
        if (epilogues.Count == 0) {
            return null;
        }
        int? bytes = null;
        foreach (int? current in epilogues.Select(int? (epilogue) => ReturnNode(epilogue.Exit) switch {
                ProcedureReturnHelperNode { Distance: CallDistance.Near } => 2,
                ProcedureReturnHelperNode { Distance: CallDistance.Far } => 4,
                InterruptReturnNode => 6,
                _ => null
            })) {
            if (current is null || (bytes is not null && current != bytes)) {
                return null;
            }
            bytes = current;
        }
        return bytes;
    }

    /// <summary>The <c>ret n</c> / <c>retf n</c> operand when every return exit agrees (an
    /// <c>iret</c> contributes 0), else null.</summary>
    private static int? PoppedArgumentBytes(List<IrFrameEpilogue> epilogues) {
        if (epilogues.Count == 0) {
            return null;
        }
        int? popped = null;
        foreach (int? current in epilogues.Select(int? (epilogue) => ReturnNode(epilogue.Exit) switch {
                ProcedureReturnHelperNode helper => ConstantWordOf(
                    helper.Arguments.FirstOrDefault()) ?? 0,
                InterruptReturnNode => 0,
                _ => null
            })) {
            if (current is null || (popped is not null && current != popped)) {
                return null;
            }
            popped = current;
        }
        return popped;
    }

    /// <summary>The return operation of a return exit: the
    /// <see cref="ProcedureReturnHelperNode"/> a <c>ret</c> / <c>retf</c> lowers to inside its
    /// <see cref="ReturnNode"/>, or an <see cref="InterruptReturnNode"/> for an <c>iret</c>.</summary>
    private static IVisitableAstNode? ReturnNode(IrExit exit) {
        IVisitableAstNode node = exit.Transfer;
        if (exit.Transfer is ReturnNode { Value: { } value }) {
            node = value;
        }
        if (node is ProcedureReturnHelperNode or InterruptReturnNode) {
            return node;
        }
        return IrAstChildren.Descendants(node).FirstOrDefault(descendant =>
            descendant is ProcedureReturnHelperNode or InterruptReturnNode);
    }

    private static bool IsPushBp(IrStatement statement) =>
        statement.Node is StackCallNode {
            Operation: StackOperation.Push,
            Arguments: [RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                RegisterIndex: (int)RegisterIndex.BpIndex }]
        };

    private static bool IsSetBp(IrStatement statement) =>
        statement.Node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                RegisterIndex: (int)RegisterIndex.BpIndex },
            Right: RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                RegisterIndex: (int)RegisterIndex.SpIndex }
        };

    private static bool IsPopBp(IrStatement statement) =>
        statement.Node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                RegisterIndex: (int)RegisterIndex.BpIndex },
            Right: ValueNode right
        } && ContainsPop(right);

    private static bool ContainsPop(IVisitableAstNode node) =>
        node is StackCallValueNode { Operation: StackOperation.Pop }
        || IrAstChildren.Descendants(node)
            .OfType<StackCallValueNode>()
            .Any(call => call.Operation == StackOperation.Pop);

    /// <summary>Whether the <c>mov sp,bp</c> of an epilogue reads the frame BP: its BP version is
    /// an SP copy at -2.</summary>
    private bool IsSetBpFromCopy(IrStatement statement) =>
        statement.Node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                RegisterIndex: (int)RegisterIndex.SpIndex },
            Right: RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                RegisterIndex: (int)RegisterIndex.BpIndex }
        } && SpCopyDelta(IrSsaValue.Ebp, _ssa.VersionUsed(statement, IrSsaValue.Ebp)) == -2;

    private static bool WritesFrameRegister(IrStatement statement) =>
        ((statement.Effects.MustWrite | statement.Effects.MayWrite) & FrameValues)
        != CpuValues.None;

    private static bool TouchesFrameRegister(IrStatement statement) =>
        ((statement.Effects.Reads | statement.Effects.MustWrite | statement.Effects.MayWrite)
         & FrameValues) != CpuValues.None;

    private static bool IsInterruptCall(CfgInstruction instruction) =>
        instruction.ExecutionAst is InterruptCallNode
        || IrAstChildren.Descendants(instruction.ExecutionAst).OfType<InterruptCallNode>().Any();

    private static int? ConstantWordOf(IVisitableAstNode? node) => node switch {
        ConstantNode constant => (int)constant.Convert(DataType.INT32),
        InstructionFieldNode field => (int)new ConstantNode(field.DataType, field.ConstantValue)
            .Convert(DataType.INT32),
        TypeConversionNode { Value: ValueNode inner } => ConstantWordOf(inner),
        ExplicitCastNode { Value: IVisitableAstNode inner } => ConstantWordOf(inner),
        _ => null
    };
}
