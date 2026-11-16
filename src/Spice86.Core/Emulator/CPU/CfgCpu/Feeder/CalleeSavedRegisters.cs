namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Computes the registers a callee provably saves and restores: pushed on its straight-line
/// entry run (<c>push R</c>, <c>pusha</c>), popped back in mirror order on the straight-line
/// run before every one of its returns (<c>pop R</c>, <c>popa</c>, <c>leave</c>), with the stack
/// depth at each pop matching the depth its slot was pushed at. A register whose save or
/// restore cannot be proven on every path is not saved. SP and CS are never saved.
/// </summary>
internal static class CalleeSavedRegisters {
    private const ushort Sp = (ushort)RegisterBits.Sp;
    private const ushort Bp = (ushort)RegisterBits.Bp;
    private const ushort Cs = (ushort)RegisterBits.Cs;

    /// <summary>One entry-run stack slot: the register bits its push saved (0 when the push
    /// cannot hold a saved register), the stack depth right after the push, and whether the
    /// push was a <c>pusha</c>.</summary>
    private sealed record Slot(ushort Mask, int DepthAfter, bool IsPushAll);

    /// <summary>One stack state a depth-check path carries.</summary>
    private readonly record struct PathState(CfgInstruction Node, int Pushed, int? Frame);

    /// <summary>What a return-run pop does to one slot: which slot it matches and the register
    /// bits the match grants, or 0 when the pop restores nothing.</summary>
    private readonly record struct PopBinding(int SlotIndex, bool IsLeave, ushort Grant);

    /// <summary>One pop found in a return run: the register bit it restores (<c>pop R</c>, BP
    /// for <c>leave</c>), whether it is a <c>popa</c>, and whether it is a stack pop of a shape
    /// that restores nothing (<c>pop r/m</c>, ...).</summary>
    private sealed record Pop(CfgInstruction Node, ushort Bit, bool IsPopAll, bool IsLeave, bool IsOther);

    /// <summary>
    /// The <see cref="RegisterBits"/> mask of registers <paramref name="entry"/> saves and
    /// restores, or 0. <paramref name="visited"/> is the node set the write scan already
    /// accepted and <paramref name="acrossMasks"/> the across-mask it computed for every call
    /// or int node in that set; both are reused as-is.
    /// </summary>
    public static ushort Compute(SegmentedAddress entry, CfgNodeIndex nodeIndex,
        IReadOnlyCollection<CfgInstruction> visited, IReadOnlyDictionary<CfgInstruction, ushort> acrossMasks) {
        List<Slot>? slots = EntryRun(entry, nodeIndex, out List<CfgInstruction> run, out int lastSaving);
        if (slots is null) {
            return 0;
        }
        if (ReenteredEntryRun(run, lastSaving, visited, nodeIndex)) {
            return 0;
        }
        Dictionary<CfgInstruction, List<CfgInstruction>> predecessors = Predecessors(visited, nodeIndex);
        Dictionary<CfgInstruction, List<PopBinding>> bindings = new();
        ushort saved = 0xFFFF;
        int returns = 0;
        foreach (CfgInstruction node in visited) {
            if (!node.IsReturn) {
                continue;
            }
            returns++;
            saved &= RestoredByReturn(node, slots, predecessors, nodeIndex, bindings);
        }
        if (returns == 0 || saved == 0) {
            return 0;
        }
        return (ushort)(saved & ~VerifyDepths(entry, visited, acrossMasks, slots, bindings, nodeIndex)
            & ~Sp & ~Cs);
    }

    /// <summary>Rule 2: walks the entry's straight-line run, recording one <see cref="Slot"/>
    /// per push it understands. Null when the entry is not a single node or a stack effect
    /// cannot be tracked; the caller maps that to "nothing saved".</summary>
    private static List<Slot>? EntryRun(SegmentedAddress entry, CfgNodeIndex nodeIndex,
        out List<CfgInstruction> run, out int lastSaving) {
        run = [];
        lastSaving = -1;
        List<CfgInstruction> starts = [..nodeIndex.GetAtAddress(entry)];
        if (starts.Count != 1) {
            return null;
        }
        List<Slot> slots = [];
        ushort writtenBefore = 0;
        int pushed = 0;
        int? frame = null;
        CfgInstruction node = starts[0];
        while (!IsTransfer(node)) {
            run.Add(node);
            ushort writes = RegisterWriteSet.MayWrite(node);
            if ((writes & (Sp | Bp)) != 0
                && !CalleeReturnScanner.ApplyStackEffect(node, ref pushed, ref frame)) {
                return null;
            }
            foreach (IVisitableAstNode statement in Statements(node)) {
                if (statement is StackCallNode { Operation: StackOperation.Push or StackOperation.PushAll } push) {
                    Slot slot = new(PushSlotMask(push, writtenBefore), pushed, push.Operation == StackOperation.PushAll);
                    slots.Add(slot);
                    if (slot.Mask != 0) {
                        lastSaving = run.Count - 1;
                    }
                }
            }
            writtenBefore |= (ushort)(writes & ~Sp);
            if (node.StaticSuccessorAddresses.Count != 1) {
                break;
            }
            List<CfgInstruction> next = [..nodeIndex.GetAtAddress(node.StaticSuccessorAddresses[0])];
            if (next.Count != 1) {
                break;
            }
            node = next[0];
        }
        return lastSaving < 0 ? null : slots;
    }

    /// <summary>The register bits a push statement saves: the pushed 16-bit register (not SP),
    /// the pushed segment register (not CS), or the <c>pusha</c> set, minus anything already
    /// written earlier in the run. 0 when the push cannot hold a saved register.</summary>
    private static ushort PushSlotMask(StackCallNode push, ushort writtenBefore) {
        if (push.Width != BitWidth.WORD_16 || push.Operation == StackOperation.PushAll) {
            return push.Operation == StackOperation.PushAll && push.Width == BitWidth.WORD_16
                ? (ushort)((ushort)(RegisterBits.AllGeneralPurpose & ~RegisterBits.Sp) & ~writtenBefore)
                : (ushort)0;
        }
        IVisitableAstNode argument = push.Arguments.Count == 1 ? push.Arguments[0] : push;
        argument = argument is TypeConversionNode conversion ? conversion.Value : argument;
        return argument switch {
            SegmentRegisterNode segment => segment.RegisterIndex == (int)SegmentRegisterIndex.CsIndex
                ? (ushort)0
                : MaskFor(8 + segment.RegisterIndex, writtenBefore),
            RegisterNode register => MaskFor(register.RegisterIndex, writtenBefore),
            _ => 0
        };
        static ushort MaskFor(int index, ushort writtenBefore) {
            ushort bit = (ushort)(1 << index);
            return index == (int)RegisterIndex.SpIndex || (bit & writtenBefore) != 0 ? (ushort)0 : bit;
        }
    }

    private static bool IsTransfer(CfgInstruction node) => node.IsCall || node.IsJump || node.IsReturn;

    /// <summary>The scan's edges out of one node: static successors, plus the call
    /// continuation for call/int nodes.</summary>
    private static IEnumerable<SegmentedAddress> ScanSuccessors(CfgInstruction node) {
        foreach (SegmentedAddress successor in node.StaticSuccessorAddresses) {
            yield return successor;
        }
        if (node.IsCall) {
            yield return node.NextInMemoryAddress32.ToSegmentedAddress();
        }
    }

    /// <summary>Back-edge guard: no visited node may land on an entry-run node up to and
    /// including the last saving push, except the run's own fall-through.</summary>
    private static bool ReenteredEntryRun(List<CfgInstruction> run, int lastSaving,
        IReadOnlyCollection<CfgInstruction> visited, CfgNodeIndex nodeIndex) {
        Dictionary<CfgInstruction, int> position = new();
        for (int i = 0; i <= lastSaving; i++) {
            position[run[i]] = i;
        }
        foreach (CfgInstruction node in visited) {
            foreach (SegmentedAddress successor in ScanSuccessors(node)) {
                foreach (CfgInstruction target in nodeIndex.GetAtAddress(successor)) {
                    if (position.TryGetValue(target, out int i) && (i == 0 || node != run[i - 1])) {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>Node-level predecessor map over the visited set, by scan successor.</summary>
    private static Dictionary<CfgInstruction, List<CfgInstruction>> Predecessors(
        IReadOnlyCollection<CfgInstruction> visited, CfgNodeIndex nodeIndex) {
        Dictionary<CfgInstruction, List<CfgInstruction>> predecessors = new();
        foreach (CfgInstruction node in visited) {
            foreach (SegmentedAddress successor in ScanSuccessors(node)) {
                foreach (CfgInstruction target in nodeIndex.GetAtAddress(successor)) {
                    if (!predecessors.TryGetValue(target, out List<CfgInstruction>? list)) {
                        list = predecessors[target] = [];
                    }
                    list.Add(node);
                }
            }
        }
        return predecessors;
    }

    /// <summary>Rule 3: the register bits one return node provably restores. Walks the
    /// straight-line run ending at <paramref name="ret"/> backwards, lists its pops in order,
    /// requires them to mirror the slots, and grants each matched slot's registers minus what
    /// later run nodes write. Records a <see cref="PopBinding"/> per granted pop for the depth
    /// check.</summary>
    private static ushort RestoredByReturn(CfgInstruction ret, List<Slot> slots,
        Dictionary<CfgInstruction, List<CfgInstruction>> predecessors, CfgNodeIndex nodeIndex,
        Dictionary<CfgInstruction, List<PopBinding>> bindings) {
        List<CfgInstruction> run = [];
        HashSet<CfgInstruction> seen = [ret];
        CfgInstruction current = ret;
        while (true) {
            int count = 0;
            foreach (CfgInstruction _ in nodeIndex.GetAtAddress(current.Address)) {
                count++;
            }
            if (count != 1
                || !predecessors.TryGetValue(current, out List<CfgInstruction>? list) || list.Count != 1) {
                break;
            }
            CfgInstruction predecessor = list[0];
            if (IsTransfer(predecessor) || predecessor.StaticSuccessorAddresses.Count != 1
                || !seen.Add(predecessor)) {
                break;
            }
            run.Add(predecessor);
            current = predecessor;
        }
        run.Reverse();
        List<Pop> pops = [];
        bool pushAfterPop = false;
        foreach (CfgInstruction runNode in run) {
            foreach (IVisitableAstNode statement in Statements(runNode)) {
                if (statement is StackCallNode { Operation: StackOperation.Push or StackOperation.PushAll }) {
                    pushAfterPop |= pops.Count > 0;
                    continue;
                }
                if (PoppedRegister(runNode, statement) is { } popped) {
                    pops.Add(popped);
                }
            }
        }
        if (pushAfterPop || pops.Count != slots.Count) {
            return 0;
        }
        ushort[] suffix = new ushort[run.Count + 1];
        for (int i = run.Count - 1; i >= 0; i--) {
            suffix[i] = (ushort)(suffix[i + 1] | RegisterWriteSet.MayWrite(run[i]));
        }
        Dictionary<CfgInstruction, int> runPosition = new();
        for (int i = 0; i < run.Count; i++) {
            runPosition[run[i]] = i;
        }
        ushort restored = 0;
        for (int i = 0; i < pops.Count; i++) {
            Pop pop = pops[i];
            int slotIndex = pops.Count - 1 - i;
            ushort grant = (ushort)(Grant(slots[slotIndex], pop)
                & ~suffix[runPosition[pop.Node] + 1]);
            if (grant == 0) {
                continue;
            }
            restored |= grant;
            if (!bindings.TryGetValue(pop.Node, out List<PopBinding>? list)) {
                list = bindings[pop.Node] = [];
            }
            list.Add(new PopBinding(slotIndex, pop.IsLeave, grant));
        }
        return restored;
    }

    /// <summary>The register bits <paramref name="pop"/> restores when it sits at the mirrored
    /// position of <paramref name="slot"/>: a register pop against a Register slot, a
    /// <c>popa</c> against a PushAll slot, <c>leave</c> against a saved BP. Anything else
    /// restores nothing.</summary>
    private static ushort Grant(Slot slot, Pop pop) {
        if (slot.Mask == 0) {
            return 0;
        }
        if (pop.IsPopAll) {
            return slot.IsPushAll ? slot.Mask : (ushort)0;
        }
        if (pop.IsLeave) {
            return slot.Mask == Bp ? Bp : (ushort)0;
        }
        return pop.Bit != 0 && slot.Mask == pop.Bit ? pop.Bit : (ushort)0;
    }

    /// <summary>Recognises one popped register in a statement: <c>pop R</c> (16-bit register,
    /// or segment register through <c>SS:[SP]</c>), <c>popa</c> or <c>leave</c> (a BP pop).
    /// Any other stack pop counts but restores nothing.</summary>
    private static Pop? PoppedRegister(CfgInstruction node, IVisitableAstNode statement) {
        switch (statement) {
            case StackCallNode { Operation: StackOperation.PopAll, Width: BitWidth.WORD_16 }:
                return new Pop(node, 0, true, false, false);
            case StackCallNode { Operation: StackOperation.Leave }:
                return new Pop(node, Bp, false, true, false);
            case BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: SegmentRegisterNode segment,
                    Right: SegmentedPointerNode {
                        Segment: SegmentRegisterNode { RegisterIndex: (int)SegmentRegisterIndex.SsIndex },
                        Offset: RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex }
                    }
                }:
                return new Pop(node, (ushort)(1 << (8 + segment.RegisterIndex)), false, false, false);
            case BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: RegisterNode register,
                    Right: var value
                } when ContainsPop(value):
                return new Pop(node, (ushort)(1 << register.RegisterIndex), false, false, false);
            case VariableDeclarationNode { Initializer: var initializer } when ContainsPop(initializer):
                return new Pop(node, 0, false, false, true);
            default:
                return null;
        }
    }

    private static bool ContainsPop(IVisitableAstNode node) => node switch {
        StackCallValueNode { Operation: StackOperation.Pop } => true,
        BinaryOperationNode binary => ContainsPop(binary.Left) || ContainsPop(binary.Right),
        _ => false
    };

    private static IEnumerable<IVisitableAstNode> Statements(CfgInstruction node) =>
        node.ExecutionAst is BlockNode block ? block.Statements : [node.ExecutionAst!];

    /// <summary>Rule 4: replays every path over the visited nodes tracking stack depth, and
    /// returns the register bits whose pops did not provably read their slot: a pop of a
    /// matched slot must find the depth the slot was pushed at (<c>leave</c>: the frame).</summary>
    private static ushort VerifyDepths(SegmentedAddress entry, IReadOnlyCollection<CfgInstruction> visited,
        IReadOnlyDictionary<CfgInstruction, ushort> acrossMasks, List<Slot> slots,
        Dictionary<CfgInstruction, List<PopBinding>> bindings, CfgNodeIndex nodeIndex) {
        ushort failed = 0;
        HashSet<PathState> seen = [];
        Stack<PathState> work = new();
        foreach (CfgInstruction start in nodeIndex.GetAtAddress(entry)) {
            work.Push(new PathState(start, 0, null));
        }
        while (work.Count > 0) {
            PathState state = work.Pop();
            if (!seen.Add(state)) {
                continue;
            }
            if (seen.Count > 4 * CalleeReturnScanner.MaxNodes) {
                return 0xFFFF;
            }
            CfgInstruction node = state.Node;
            if (node.IsReturn) {
                continue;
            }
            int pushed = state.Pushed;
            int? frame = state.Frame;
            if (bindings.TryGetValue(node, out List<PopBinding>? list)) {
                foreach (PopBinding binding in list) {
                    bool ok = binding.IsLeave
                        ? frame == slots[binding.SlotIndex].DepthAfter
                        : pushed == slots[binding.SlotIndex].DepthAfter;
                    if (!ok) {
                        failed |= binding.Grant;
                    }
                }
            }
            if (node.IsCall) {
                ushort across = acrossMasks[node];
                if ((across & Sp) != 0) {
                    return 0xFFFF;
                }
                if ((across & Bp) != 0) {
                    frame = null;
                }
                PushTargets(work, nodeIndex.GetAtAddress(node.NextInMemoryAddress32.ToSegmentedAddress()), pushed, frame);
                continue;
            }
            ushort writes = RegisterWriteSet.MayWrite(node);
            if ((writes & (Sp | Bp)) != 0
                && !CalleeReturnScanner.ApplyStackEffect(node, ref pushed, ref frame)) {
                return 0xFFFF;
            }
            if ((writes & Bp) != 0 && !IsFrameStatement(node)) {
                frame = null;
            }
            foreach (SegmentedAddress successor in node.StaticSuccessorAddresses) {
                PushTargets(work, nodeIndex.GetAtAddress(successor), pushed, frame);
            }
        }
        return failed;
    }

    private static void PushTargets(Stack<PathState> work, IEnumerable<CfgInstruction> targets,
        int pushed, int? frame) {
        foreach (CfgInstruction target in targets) {
            work.Push(new PathState(target, pushed, frame));
        }
    }

    /// <summary>Whether a BP-writing node is the frame pair (<c>mov bp,sp</c>) or
    /// <c>leave</c>; any other BP write breaks the tracked frame.</summary>
    private static bool IsFrameStatement(CfgInstruction node) {
        foreach (IVisitableAstNode statement in Statements(node)) {
            if (statement is StackCallNode { Operation: StackOperation.Leave }
                || statement is BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: RegisterNode { RegisterIndex: (int)RegisterIndex.BpIndex },
                    Right: RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex }
                }) {
                return true;
            }
        }
        return false;
    }
}
