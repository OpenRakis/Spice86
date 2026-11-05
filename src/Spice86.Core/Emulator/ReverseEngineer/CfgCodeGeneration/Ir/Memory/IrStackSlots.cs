namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Statement-level stack-slot tracking over an <see cref="IrMethod"/>: the same forward analysis
/// the CFG-level stack-slot tracking ran on CFG nodes, ported to the IR statements after the
/// pass pipeline. For every
/// statement it records the SP <see cref="IrStackOffset"/> before and after it and the stack
/// reads it performs with the slot definitions that may have supplied them
/// (<see cref="IrStackSlotRead"/>).
/// <para>A push below the current delta defines a slot; a pop or an <c>SS:[SP + displacement]</c>
/// pointer read reads the slot at that delta; a peek of constant argument k reads at
/// <c>delta + k</c>; a constant SP/ESP adjustment moves the delta (an increase frees the slots it
/// passes); a <c>Leave</c>, <c>PushAll</c>, <c>PopAll</c>, any other SP/ESP or SS write, and any
/// <see cref="IrMemoryAliasAnalysis.IsFullMemoryBoundary"/> statement rebase SP on the statement.
/// Every memory write of a statement kills the slots its access is not
/// <see cref="IrAliasRelation.Disjoint"/> with, against the synthetic
/// <see cref="IrMemoryAliasAnalysis.StackAccess"/> of the slot. A slot defined on some reaching
/// paths only is partial: its reads report the union of definitions but are not
/// <see cref="IrStackSlotRead.Complete"/>. Reads keep being recorded with no
/// definitions while the offset is unknown; only rebasing gives a fresh base.</para>
/// <para>The state also tracks the <see cref="IrStackOffset"/> BP holds: <c>mov bp, sp</c> copies
/// the SP offset, any statement that may write a byte of BP or SS (calls, pops, event checks,
/// SS writes, rebases) forgets it, and a <c>mov sp, bp</c> on the same base restores SP to the
/// copy's delta instead of rebasing. An <c>SS:[BP + displacement]</c> write kills a slot only
/// when its byte range overlaps the slot's, so a standard frame's argument writes do not kill
/// the saved-BP slot the epilogue's <c>pop bp</c> restores.</para>
/// </summary>
internal sealed class IrStackSlots {
    private static readonly CpuValues StackWrites = CpuValues.Esp | CpuValues.Ss;

    private readonly Dictionary<IrStatement, IrStackOffset> _before = [];
    private readonly Dictionary<IrStatement, IrStackOffset> _after = [];
    private readonly Dictionary<IrBlock, IrStackOffset> _blockEntry = [];
    private readonly Dictionary<IrBlock, IrStackOffset> _blockEnd = [];
    private readonly Dictionary<IrStatement, List<IrStackSlotRead>> _readsByStatement = [];
    private readonly Dictionary<IrStackSlotDefinition, List<IrStackSlotRead>> _readsByDefinition = [];

    /// <summary>Runs the analysis on <paramref name="method"/>, using
    /// <paramref name="alias"/> as the kill oracle.</summary>
    public static IrStackSlots Compute(IrMethod method, IrMemoryAliasAnalysis alias) {
        IrStackSlots slots = new(alias);
        BlockGraph<IrBlock> graph = method.ToStructuringGraph();
        DepthFirstOrdering<IrBlock> traversal = new(
            [.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        ForwardDataflowResult<IrBlock, IrStackState> result = ForwardDataflowSolver.Solve(
            graph, traversal,
            (block, state) => slots.TransferBlock(block, state, false),
            IrStackState.Join, IrStackState.Unreached, IrStackState.Entry);
        foreach (IrBlock block in graph.Blocks) {
            slots.TransferBlock(block, result.In[block], true);
        }
        return slots;
    }

    /// <summary>The slots of a method that holds no stack node at all: no read is ever recorded.
    /// </summary>
    internal static readonly IrStackSlots Empty = new(null);

    private readonly IrMemoryAliasAnalysis? _alias;

    private IrStackSlots(IrMemoryAliasAnalysis? alias) {
        _alias = alias;
    }

    /// <summary>The SP offset before <paramref name="statement"/>, null when the analysis has not
    /// seen it.</summary>
    public IrStackOffset? OffsetBefore(IrStatement statement) =>
        _before.TryGetValue(statement, out IrStackOffset offset) ? offset : null;

    /// <summary>The SP offset after <paramref name="statement"/>, null when the analysis has not
    /// seen it.</summary>
    public IrStackOffset? OffsetAfter(IrStatement statement) =>
        _after.TryGetValue(statement, out IrStackOffset offset) ? offset : null;

    /// <summary>The SP offset at <paramref name="block"/>'s entry, null when the analysis has not
    /// seen it.</summary>
    public IrStackOffset? OffsetAtBlockEntry(IrBlock block) =>
        _blockEntry.TryGetValue(block, out IrStackOffset offset) ? offset : null;

    /// <summary>The SP offset at <paramref name="block"/>'s end, after its statements and before
    /// its terminator's effects - the offset a <c>ret</c> terminator reads.</summary>
    public IrStackOffset? OffsetAtBlockEnd(IrBlock block) =>
        _blockEnd.TryGetValue(block, out IrStackOffset offset) ? offset : null;

    /// <summary>The stack reads <paramref name="statement"/> performs.</summary>
    public IReadOnlyList<IrStackSlotRead> ReadsOf(IrStatement statement) =>
        _readsByStatement.TryGetValue(statement, out List<IrStackSlotRead>? reads) ? reads : [];

    /// <summary>The reads that may observe <paramref name="definition"/>.</summary>
    public IReadOnlyList<IrStackSlotRead> ReadsOf(IrStackSlotDefinition definition) =>
        _readsByDefinition.TryGetValue(definition, out List<IrStackSlotRead>? reads) ? reads : [];

    private IrStackState TransferBlock(IrBlock block, IrStackState state, bool record) {
        if (record) {
            _blockEntry[block] = state.Offset;
        }
        for (int index = 0; index < block.Statements.Count; index++) {
            state = TransferStatement(block, index, state, record);
        }
        if (record) {
            _blockEnd[block] = state.Offset;
        }
        IrTerminator terminator = block.Terminator;
        if (((terminator.Effects.MustWrite | terminator.Effects.MayWrite) & StackWrites)
            != CpuValues.None) {
            // The terminator has no statement to rebase on: conservatively drop the base.
            state = IrStackState.OfOffset(IrStackOffset.Unknown);
        }
        return state;
    }

    private IrStackState TransferStatement(IrBlock block, int index, IrStackState state, bool record) {
        IrStatement statement = block.Statements[index];
        IrMemoryAliasAnalysis alias =
            _alias ?? throw new InvalidOperationException("The empty slots hold no analysis.");
        if (record) {
            _before[statement] = state.Offset;
        }
        state = TransferredState(block, index, statement, state, record, alias);
        if (record) {
            _after[statement] = state.Offset;
        }
        return state;
    }

    private IrStackState TransferredState(IrBlock block, int index, IrStatement statement,
        IrStackState state, bool record, IrMemoryAliasAnalysis alias) {
        if (alias.IsFullMemoryBoundary(block, index)) {
            return IrStackState.Rebase(statement);
        }
        return ApplyStatement(block, index, statement, state, record, alias);
    }

    private IrStackState ApplyStatement(IrBlock block, int index, IrStatement statement,
        IrStackState state, bool record, IrMemoryAliasAnalysis alias) {
        IrStackState applied = ApplyStatementEffect(block, index, statement, state, record, alias);
        return TrackBp(statement, applied);
    }

    /// <summary>The BP copy update every statement gets: <c>mov bp, sp</c> records the offset BP
    /// now equals; any other statement that may write a byte of BP or SS forgets it.</summary>
    private static IrStackState TrackBp(IrStatement statement, IrStackState state) {
        if (IsRegisterCopy(statement.Node, RegisterIndex.BpIndex, RegisterIndex.SpIndex)) {
            return state.WithBp(state.Offset);
        }
        if (((statement.Effects.MustWrite | statement.Effects.MayWrite)
                & (CpuValues.Ebp | CpuValues.Ss)) != CpuValues.None) {
            return state.WithBp(IrStackOffset.Unknown);
        }
        return state;
    }

    /// <summary>Whether <paramref name="node"/> is the 16-bit statement
    /// <c>target = source</c> of two general registers.</summary>
    private static bool IsRegisterCopy(IVisitableAstNode node, RegisterIndex targetIndex,
        RegisterIndex sourceIndex) =>
        node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } target,
            Right: RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } source }
        && target.RegisterIndex == (int)targetIndex && source.RegisterIndex == (int)sourceIndex;

    private IrStackState ApplyStatementEffect(IrBlock block, int index, IrStatement statement,
        IrStackState state, bool record, IrMemoryAliasAnalysis alias) {
        switch (statement.Node) {
            case StackCallNode { Operation: StackOperation.Push } push: {
                if (state.Offset.Kind == IrStackOffsetKind.Known) {
                    int width = (int)push.Width / 8;
                    int delta = state.Offset.Delta - width;
                    IrStackState pushed = state
                        .WithOffset(IrStackOffset.Known(state.Offset.Base, delta))
                        .CopySlots();
                    RemoveSlotsIn(pushed, delta, width);
                    pushed.Slots[delta] = [new IrStackSlotDefinition(statement, delta)];
                    pushed.PartialSlots.Remove(delta);
                    return pushed;
                }
                return state;
            }
            case StackCallNode { Operation: StackOperation.Leave
                or StackOperation.PushAll or StackOperation.PopAll }:
                return IrStackState.Rebase(statement);
        }
        List<StackCallValueNode> stackValues = [.. IrAstChildren.Descendants(statement.Node)
            .OfType<StackCallValueNode>()];
        if (stackValues.Count > 0) {
            IrStackOffset before = state.Offset;
            foreach (StackCallValueNode stack in stackValues) {
                state = ApplyStackValue(stack, statement, state, record);
            }
            return KillWrittenSlots(block, index, statement, state, before, alias);
        }
        RecordPointerReads(block, index, statement, state, record, alias);
        IrStackOffset beforeAdjust = state.Offset;
        if (TryStackPointerAdjustment(statement.Node, out int adjustment)) {
            state = AdjustDelta(state, adjustment);
        } else if (IsRegisterCopy(statement.Node, RegisterIndex.SpIndex, RegisterIndex.BpIndex)
            && state.Offset.Kind == IrStackOffsetKind.Known
            && state.Bp.Kind == IrStackOffsetKind.Known
            && ReferenceEquals(state.Bp.Base, state.Offset.Base)) {
            // mov sp, bp with a trusted BP copy restores SP to the copy's delta on the same base.
            state = AdjustDelta(state, state.Bp.Delta - state.Offset.Delta);
        } else if (WritesStackPointerOrSegment(statement)) {
            // pop ss / pop sp read before rebasing; mov sp, bp without a trusted BP copy and the
            // like lose the base.
            state = IrStackState.Rebase(statement);
        }
        return KillWrittenSlots(block, index, statement, state, beforeAdjust, alias);
    }

    private IrStackState ApplyStackValue(StackCallValueNode stack, IrStatement statement,
        IrStackState state, bool record) {
        switch (stack.Operation) {
            case StackOperation.Pop: {
                int width = (int)stack.Width / 8;
                if (record) {
                    RecordRead(statement, state.Offset.Delta, width, state);
                }
                if (state.Offset.Kind != IrStackOffsetKind.Known) {
                    return state;
                }
                int delta = state.Offset.Delta;
                IrStackState moved = state
                    .WithOffset(IrStackOffset.Known(state.Offset.Base, delta + width))
                    .CopySlots();
                RemoveSlotsIn(moved, delta, width);
                return moved;
            }
            case StackOperation.Peek:
                if (record && ConstantWordOf(stack.CallNode.Arguments
                    .OfType<ValueNode>().FirstOrDefault()) is { } peekDelta) {
                    RecordRead(statement, state.Offset.Delta + peekDelta,
                        (int)stack.Width / 8, state);
                }
                return state;
            default:
                return state;
        }
    }

    private void RecordPointerReads(IrBlock block, int index, IrStatement statement,
        IrStackState state, bool record, IrMemoryAliasAnalysis alias) {
        if (!record) {
            return;
        }
        foreach (IrMemoryAccess read in alias.Reads(block, index)
                .Where(read => read is { SegmentRegister: IrSsaValue.Ss, OffsetTerms: [
                    { Register: RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                        RegisterIndex: (int)RegisterIndex.SpIndex } }] })) {
            RecordRead(statement, state.Offset.Delta + read.OffsetDisplacement, read.Width, state);
        }
    }

    /// <summary>Removes every slot whose byte range intersects <c>[start, start + width)</c>: a
    /// push or pop consumes those bytes, whatever delta the slot starts at.</summary>
    private static void RemoveSlotsIn(IrStackState state, int start, int width) {
        List<int> removed = [];
        foreach ((int delta, HashSet<IrStackSlotDefinition> definitions) in state.Slots) {
            if (delta < start + width && start < delta + SlotWidth(definitions)) {
                removed.Add(delta);
            }
        }
        foreach (int delta in removed) {
            state.Slots.Remove(delta);
            state.PartialSlots.Remove(delta);
        }
    }

    /// <summary>Kills the slots a write of the statement touches. The slot accesses compare
    /// against the SP version the statement reads - the version before it - so the displacement
    /// uses <paramref name="before"/>, the offset the statement started with (a pop's own SP move
    /// must not shift it).</summary>
    private IrStackState KillWrittenSlots(IrBlock block, int index, IrStatement statement,
        IrStackState state, IrStackOffset before, IrMemoryAliasAnalysis alias) {
        if (state.Offset.Kind != IrStackOffsetKind.Known || state.Slots.Count == 0) {
            return state;
        }
        int beforeDelta = before.Kind == IrStackOffsetKind.Known ? before.Delta : state.Offset.Delta;
        ushort? stackPointer = null;
        if (state.Offset.Base is { } baseStatement) {
            stackPointer = alias.StackPointerConstantAfter(baseStatement);
        }
        List<int> killed = [];
        foreach (IrMemoryAccess write in alias.Writes(block, index)) {
            IrMemoryAccess stackWrite = write;
            if (write is { SegmentRegister: IrSsaValue.Ss, OffsetTerms: [
                    { Register: RegisterNode { DataType.BitWidth: BitWidth.WORD_16,
                        RegisterIndex: (int)RegisterIndex.BpIndex } }] }
                && state.Bp.Kind == IrStackOffsetKind.Known
                && ReferenceEquals(state.Bp.Base, state.Offset.Base)) {
                // A trusted BP copy makes SS:[BP + c] a stack access at delta Bp.Delta + c:
                // express it SP-relative like the slots so the circular relation compares the
                // wrapped byte ranges (a negative c normalizes to a high displacement).
                int bpDelta = state.Bp.Delta + write.OffsetDisplacement;
                if (stackPointer is { } known) {
                    stackWrite = alias.ConstantStackAccess(block, index, known + bpDelta,
                        write.Width);
                } else {
                    stackWrite = alias.StackAccess(block, index, bpDelta - beforeDelta,
                        write.Width);
                }
            }
            foreach ((int delta, HashSet<IrStackSlotDefinition> definitions) in state.Slots) {
                if (killed.Contains(delta)) {
                    continue;
                }
                IrMemoryAccess slot = SlotAccess(block, index, stackPointer, delta, beforeDelta,
                    definitions, alias);
                if (alias.Relation(stackWrite, slot) != IrAliasRelation.Disjoint) {
                    killed.Add(delta);
                }
            }
        }
        if (killed.Count == 0) {
            return state;
        }
        IrStackState shrunk = state.CopySlots();
        foreach (int delta in killed) {
            shrunk.Slots.Remove(delta);
            shrunk.PartialSlots.Remove(delta);
        }
        return shrunk;
    }

    private IrMemoryAccess SlotAccess(IrBlock block, int index, ushort? stackPointer, int delta,
        int beforeDelta, HashSet<IrStackSlotDefinition> definitions, IrMemoryAliasAnalysis alias) {
        if (stackPointer is { } known) {
            return alias.ConstantStackAccess(block, index, known + delta, SlotWidth(definitions));
        }
        return alias.StackAccess(block, index, delta - beforeDelta, SlotWidth(definitions));
    }

    private static int SlotWidth(HashSet<IrStackSlotDefinition> definitions) {
        if (definitions.Count > 0
            && definitions.First().Statement.Node is StackCallNode
                { Operation: StackOperation.Push } push) {
            return (int)push.Width / 8;
        }
        return 2;
    }

    private IrStackState AdjustDelta(IrStackState state, int adjustment) {
        if (state.Offset.Kind != IrStackOffsetKind.Known) {
            return state;
        }
        int delta = state.Offset.Delta + adjustment;
        state = state.WithOffset(IrStackOffset.Known(state.Offset.Base, delta));
        return adjustment > 0 ? state.WithoutSlotsBelow(delta) : state;
    }

    /// <summary>Records a read of <paramref name="width"/> bytes at <paramref name="delta"/>: only
    /// a slot starting there with the same width supplies it; a slot of another width leaves the
    /// read unattributed.</summary>
    private void RecordRead(IrStatement statement, int delta, int width, IrStackState state) {
        HashSet<IrStackSlotDefinition> definitions = new(state.DefinitionsOf(delta));
        if (definitions.Count > 0 && SlotWidth(definitions) != width) {
            definitions = [];
        }
        IrStackSlotRead read = new(statement, delta, definitions,
            definitions.Count > 0 && !state.PartialSlots.Contains(delta));
        if (!_readsByStatement.TryGetValue(statement, out List<IrStackSlotRead>? reads)) {
            reads = [];
            _readsByStatement[statement] = reads;
        }
        reads.Add(read);
        foreach (IrStackSlotDefinition definition in read.Definitions) {
            if (!_readsByDefinition.TryGetValue(definition, out List<IrStackSlotRead>? readers)) {
                readers = [];
                _readsByDefinition[definition] = readers;
            }
            readers.Add(read);
        }
    }

    /// <summary>Whether the statement writes SP or ESP - fully or partially, including a may -
    /// or SS.</summary>
    private static bool WritesStackPointerOrSegment(IrStatement statement) =>
        ((statement.Effects.MustWrite | statement.Effects.MayWrite) & StackWrites)
        != CpuValues.None;

    /// <summary>The constant adjustment of <c>SP = SP +/- c</c> or <c>SP = Alu.Add/Sub(SP, c)</c>
    /// (16 or 32-bit SP), signed so an add of a negative constant is a decrease.</summary>
    internal static bool TryStackPointerAdjustment(IVisitableAstNode node, out int adjustment) {
        adjustment = 0;
        if (node is not BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex } target } assign) {
            return false;
        }
        return TryAdjustment(assign.Right, target, ref adjustment);
    }

    private static bool TryAdjustment(ValueNode value, RegisterNode target, ref int adjustment) {
        switch (value) {
            case BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS,
                Left: RegisterNode register, Right: ValueNode operand }
                when SameRegister(register, target)
                && ConstantWordOf(operand) is { } added:
                adjustment = added;
                return true;
            case BinaryOperationNode { BinaryOperation: BinaryOperation.MINUS,
                Left: RegisterNode register, Right: ValueNode operand }
                when SameRegister(register, target)
                && ConstantWordOf(operand) is { } subtracted:
                adjustment = -subtracted;
                return true;
            case AluCallValueNode { CallNode: AluCallNode aluCall }:
                return TryAluAdjustment(aluCall, target, ref adjustment);
            case AluOperationNode { Call.CallNode: AluCallNode aluCall }:
                return TryAluAdjustment(aluCall, target, ref adjustment);
            default:
                return false;
        }
    }

    private static bool TryAluAdjustment(AluCallNode call, RegisterNode target, ref int adjustment) {
        if (call.Arguments is not [RegisterNode register, ValueNode operand]
            || !SameRegister(register, target)
            || ConstantWordOf(operand) is not { } constant) {
            return false;
        }
        switch (call.Operation) {
            case AluOperation.Add:
                adjustment = constant;
                return true;
            case AluOperation.Sub:
                adjustment = -constant;
                return true;
            default:
                return false;
        }
    }

    private static bool SameRegister(RegisterNode first, RegisterNode second) =>
        first.RegisterIndex == second.RegisterIndex && first.DataType == second.DataType;

    private static int? ConstantWordOf(ValueNode? node) => node switch {
        ConstantNode constant => (int)constant.Convert(DataType.INT32),
        InstructionFieldNode field => (int)new ConstantNode(field.DataType, field.ConstantValue)
            .Convert(DataType.INT32),
        TypeConversionNode { Value: ValueNode inner } => ConstantWordOf(inner),
        _ => null
    };
}
