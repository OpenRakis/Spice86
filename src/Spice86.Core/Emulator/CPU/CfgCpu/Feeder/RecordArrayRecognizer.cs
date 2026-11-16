namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Recognizes a <c>jmp/call [seg:R+disp]</c> site whose index register points into an array of
/// records walked by a search loop (or set to a constant record address). A bounded backward walk
/// from the site tracks R: loops (<c>add R,stride</c> under a <c>loop</c>/<c>dec cx; jne</c>
/// back-edge) give one record list per entry path, <c>mov R,imm</c> gives a constant record
/// pointer, calls are crossed through their returns or when the callee write scan clears R, and
/// every path that ends unrecognized contributes nothing.
/// </summary>
internal sealed class RecordArrayRecognizer {
    private const int MaxTransitions = 4;
    private const int MaxPaths = 32;
    private const int MaxForwardSteps = 3;
    private const int MaxCount = 256;
    private const int CxRegisterIndex = (int)RegisterIndex.CxIndex;

    /// <summary>Value of <see cref="RecordList.CountSegmentRegisterIndex"/> when the list's count
    /// is a constant and no count cell exists.</summary>
    internal const int NoCountSegmentRegister = -1;

    /// <summary>One record array or constant record: <c>Base + i*Stride</c> for
    /// <c>0 &lt;= i &lt; Count</c>. <see cref="CountConstant"/> when set is the count; otherwise the
    /// count is the word at <c>seg[CountSegmentRegisterIndex]:CountCellOffset</c>, read at
    /// evaluation time. A constant pointer is a list of stride 0 and count 1.</summary>
    internal sealed record RecordList(ushort Base, ushort Stride, ushort? CountConstant,
        int CountSegmentRegisterIndex, ushort CountCellOffset);

    /// <param name="SegmentRegisterIndex">Index into <c>State.SegmentRegisters.UInt16</c> of the
    /// segment the records and the near targets live in.</param>
    internal sealed record RecordArrayShape(int SegmentRegisterIndex, ushort Displacement,
        bool IsFar, IReadOnlyList<RecordList> Lists);

    private readonly CfgNodeIndex _nodeIndex;
    private readonly Func<CfgInstruction, ushort> _mayWriteAcross;
    private int _nodeVisits;

    public RecordArrayRecognizer(CfgNodeIndex nodeIndex, Func<CfgInstruction, ushort> mayWriteAcross) {
        _nodeIndex = nodeIndex;
        _mayWriteAcross = mayWriteAcross;
    }

    /// <summary>Recognizes <paramref name="site"/> as a record-array transfer, or returns null when
    /// the backward walk finds no record list on any path.</summary>
    public RecordArrayShape? Recognize(CfgInstruction site) {
        if (site.DisplayAst is not InstructionNode { Parameters: [SegmentedPointerNode pointer] } display
            || display.Operation is not (InstructionOperation.JMP_NEAR or InstructionOperation.CALL_NEAR
                or InstructionOperation.JMP_FAR or InstructionOperation.CALL_FAR)) {
            return null;
        }
        bool isFar = display.Operation is InstructionOperation.JMP_FAR or InstructionOperation.CALL_FAR;
        if (pointer.DataType.BitWidth != (isFar ? BitWidth.DWORD_32 : BitWidth.WORD_16)
            || !SwitchTableRecognizer.TryGetIndexExpression(pointer.Offset, out int registerIndex,
                out ushort displacement)
            || SwitchTableRecognizer.GetSegmentRegisterIndex(pointer) is not int segmentRegisterIndex) {
            return null;
        }
        List<RecordList> lists = [];
        int paths = 0;
        _nodeVisits = 0;
        Walk(site, registerIndex, 0, 0, [site], lists, ref paths);
        if (lists.Count == 0) {
            return null;
        }
        List<RecordList> deduped = lists.Distinct().ToList();
        // Constant pointers (stride 0) name single records and do not take part in the stride
        // agreement; every loop list must share one record stride.
        if (deduped.Where(list => list.Stride != 0).Select(list => list.Stride).Distinct().Count() > 1) {
            return null;
        }
        return new RecordArrayShape(segmentRegisterIndex, displacement, isFar, deduped);
    }

    // --- backward path enumeration ---

    /// <summary>The predecessors the backward walk continues at. A call whose CallToReturn edge
    /// reaches the cursor is crossed when the callee write scan clears R, and additionally entered
    /// through the returns of its single static callee (for continuations the linker never wired).
    /// Other call predecessors add nothing.</summary>
    /// <param name="PassThrough">The node does not affect R (a call the write scan clears, a
    /// return, or a callee return the path entered): continue the walk at it without consulting
    /// its write mask.</param>
    private IEnumerable<(CfgInstruction Node, bool PassThrough)> Continuations(
        CfgInstruction cursor, ushort registerBit) {
        foreach (ICfgNode predecessor in cursor.Predecessors) {
            if (predecessor is not CfgInstruction node || node is { IsLive: false, IsSpeculative: false }) {
                continue;
            }
            if (node.IsCall && HasCallToReturnTo(node, cursor)) {
                if ((_mayWriteAcross(node) & registerBit) == 0) {
                    yield return (node, true);
                }
                if (node.StaticSuccessorAddresses is [SegmentedAddress callee]) {
                    foreach (CfgInstruction returnNode in CalleeReturns(callee)) {
                        if (!cursor.Predecessors.Contains(returnNode)) {
                            yield return (returnNode, true);
                        }
                    }
                }
                continue;
            }
            yield return (node, node.IsReturn);
        }
    }

    private static bool HasCallToReturnTo(CfgInstruction callNode, ICfgNode cursor) =>
        callNode.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CallToReturn, out ISet<ICfgNode>? successors)
        && successors.Contains(cursor);

    /// <summary>Every return node of the callee's static subgraph: BFS over static successors,
    /// nested calls stepped over at their next-in-memory continuation.</summary>
    private IEnumerable<CfgInstruction> CalleeReturns(SegmentedAddress callee) {
        List<CfgInstruction> returns = [];
        Queue<SegmentedAddress> frontier = new();
        HashSet<SegmentedAddress> seen = [callee];
        frontier.Enqueue(callee);
        int nodes = 0;
        while (frontier.Count > 0 && nodes < CalleeReturnScanner.MaxNodes) {
            foreach (CfgInstruction node in _nodeIndex.GetAtAddress(frontier.Dequeue())) {
                nodes++;
                if (node.IsReturn) {
                    returns.Add(node);
                    continue;
                }
                if (node.IsCall) {
                    SegmentedAddress continuation = node.NextInMemoryAddress32.ToSegmentedAddress();
                    if (seen.Add(continuation)) {
                        frontier.Enqueue(continuation);
                    }
                    continue;
                }
                foreach (CfgInstruction successor in ForwardSuccessors(node)) {
                    if (seen.Add(successor.Address)) {
                        frontier.Enqueue(successor.Address);
                    }
                }
            }
        }
        return returns;
    }

    private void Walk(CfgInstruction cursor, int registerIndex, ushort delta, int transitions,
        HashSet<CfgInstruction> visited, List<RecordList> lists, ref int paths) {
        if (_nodeVisits >= CalleeReturnScanner.MaxNodes) {
            return;
        }
        _nodeVisits++;
        ushort registerBit = (ushort)(1 << registerIndex);
        List<(CfgInstruction Node, bool PassThrough)> continuations =
            Continuations(cursor, registerBit).ToList();
        // The budget counts paths: a node with n continuations adds n-1 branches.
        paths += Math.Max(0, continuations.Count - 1);
        foreach ((CfgInstruction predecessor, bool passThrough) in continuations) {
            if (paths > MaxPaths || visited.Contains(predecessor)) {
                continue;
            }
            int nextTransitions = transitions + TransitionCount(cursor, predecessor);
            if (nextTransitions > MaxTransitions) {
                continue;
            }
            ushort writes = passThrough ? (ushort)0 : RegisterWriteSet.MayWrite(predecessor);
            if ((writes & registerBit) == 0) {
                visited.Add(predecessor);
                Walk(predecessor, registerIndex, delta, nextTransitions, visited, lists, ref paths);
                visited.Remove(predecessor);
                continue;
            }
            if (writes == (ushort)RegisterBits.AllExceptCs) {
                continue;
            }
            if (IsMovRegisterImmediate(predecessor, registerIndex, out ushort immediate)) {
                lists.Add(new RecordList((ushort)(immediate + delta), 0, 1, NoCountSegmentRegister, 0));
                continue;
            }
            if (IsAddRegisterImmediate(predecessor, registerIndex, out ushort step)) {
                if (delta == 0 && TryLoop(predecessor, step, registerIndex, visited, lists)) {
                    continue;
                }
                visited.Add(predecessor);
                Walk(predecessor, registerIndex, (ushort)(delta + step), nextTransitions, visited, lists, ref paths);
                visited.Remove(predecessor);
            }
        }
    }

    private static int TransitionCount(CfgInstruction cursor, CfgInstruction predecessor) =>
        predecessor.ContainingBlock != cursor.ContainingBlock || predecessor.ContainingBlock is null ? 1 : 0;

    // --- loop recognition ---

    /// <summary>P is an <c>add R,stride</c> whose forward edge reaches a back-edge instruction B
    /// (<c>loop H</c>, or <c>jne H</c> right after <c>dec cx</c>) whose head H is already on the
    /// path. When the natural-loop body is clean, emits one record list per entry of H.</summary>
    private bool TryLoop(CfgInstruction step, ushort stride, int registerIndex,
        HashSet<CfgInstruction> visited, List<RecordList> lists) {
        if (FindBackEdge(step, visited) is not { } loop
            || !LoopBodyIsClean(loop.Head, loop.BackEdge, loop.Decrement, step, registerIndex)) {
            return false;
        }
        bool any = false;
        foreach (ICfgNode entryPredecessor in loop.Head.Predecessors) {
            if (entryPredecessor is not CfgInstruction entry || entry == loop.BackEdge
                || IsInRange(entry, loop.Head, loop.BackEdge)
                || entry is { IsLive: false, IsSpeculative: false }) {
                continue;
            }
            int entryPaths = 0;
            WalkEntry(entry, registerIndex, stride, lists, ref entryPaths);
            any = true;
        }
        return any;
    }

    /// <summary>Follows P forward through in-block and fall-through successors for at most
    /// <see cref="MaxForwardSteps"/> instructions looking for a back-edge whose head is on the
    /// current path.</summary>
    private (CfgInstruction Head, CfgInstruction BackEdge, CfgInstruction? Decrement)? FindBackEdge(
        CfgInstruction step, HashSet<CfgInstruction> visited) {
        List<CfgInstruction> frontier = [step];
        for (int forward = 0; forward < MaxForwardSteps && frontier.Count > 0; forward++) {
            List<CfgInstruction> next = [];
            foreach (CfgInstruction node in frontier) {
                foreach (CfgInstruction successor in ForwardSuccessors(node)) {
                    if (AsBackEdge(successor, visited) is { } loop) {
                        return loop;
                    }
                    next.Add(successor);
                }
            }
            frontier = next;
        }
        return null;
    }

    /// <summary>In-block successors plus the fall-through node.</summary>
    private IEnumerable<CfgInstruction> ForwardSuccessors(CfgInstruction node) {
        CfgInstruction? fallThrough = _nodeIndex.GetAtAddress(node.NextInMemoryAddress32.ToSegmentedAddress())
            .FirstOrDefault();
        if (fallThrough is not null) {
            yield return fallThrough;
        }
        foreach (SegmentedAddress successor in node.StaticSuccessorAddresses) {
            foreach (CfgInstruction target in _nodeIndex.GetAtAddress(successor)) {
                if (target != fallThrough) {
                    yield return target;
                }
            }
        }
    }

    /// <summary><paramref name="node"/> is a back-edge to a head already on the current path when
    /// it is <c>loop H</c> or <c>jne/jnz H</c> immediately after <c>dec cx</c>.</summary>
    private (CfgInstruction Head, CfgInstruction BackEdge, CfgInstruction? Decrement)? AsBackEdge(
        CfgInstruction node, HashSet<CfgInstruction> visited) {
        if (node.DisplayAst is not InstructionNode { Operation: var operation }
            || operation is not InstructionOperation.LOOP
                and not (InstructionOperation.JNE_SHORT or InstructionOperation.JNE_NEAR)) {
            return null;
        }
        CfgInstruction? head = null;
        foreach (SegmentedAddress successor in node.StaticSuccessorAddresses) {
            foreach (CfgInstruction target in _nodeIndex.GetAtAddress(successor)) {
                if (visited.Contains(target)) {
                    head = target;
                }
            }
        }
        if (head is null) {
            return null;
        }
        if (operation is InstructionOperation.LOOP) {
            return (head, node, node);
        }
        CfgInstruction? decrement = null;
        foreach (ICfgNode predecessor in node.Predecessors) {
            if (predecessor is CfgInstruction candidate
                && candidate.DisplayAst is InstructionNode { Operation: InstructionOperation.DEC, Parameters: [var operand] }
                && SwitchTableRecognizer.AsRegister(operand) is { RegisterIndex: CxRegisterIndex }) {
                decrement = candidate;
            }
        }
        return decrement is null ? null : (head, node, decrement);
    }

    private static bool IsInRange(CfgInstruction node, CfgInstruction head, CfgInstruction backEdge) =>
        node.Address.Segment == head.Address.Segment
        && node.Address.Offset >= head.Address.Offset
        && node.Address.Offset <= backEdge.Address.Offset;

    /// <summary>BFS from the head over successors staying in [H,B]: no node may write R except the
    /// step, and none may write CX except the loop's own decrement (<c>loop</c> or <c>dec cx</c>).</summary>
    private bool LoopBodyIsClean(CfgInstruction head, CfgInstruction backEdge,
        CfgInstruction? decrement, CfgInstruction step, int registerIndex) {
        ushort checkBits = (ushort)((1 << registerIndex) | (ushort)RegisterBits.Cx);
        Queue<CfgInstruction> frontier = new([head]);
        HashSet<CfgInstruction> seen = [head];
        while (frontier.Count > 0) {
            CfgInstruction node = frontier.Dequeue();
            if (node != step && node != backEdge && node != decrement
                && (RegisterWriteSet.MayWrite(node) & checkBits) != 0) {
                return false;
            }
            foreach (CfgInstruction successor in ForwardSuccessors(node)) {
                if (IsInRange(successor, head, backEdge) && seen.Add(successor)) {
                    frontier.Enqueue(successor);
                }
            }
        }
        return true;
    }

    // --- entry walk: the value of R and the count at a loop entry ---

    /// <summary>How the entry path proved the loop count.</summary>
    private enum CountKind { None, Constant, Cell, CellThroughRegister }

    /// <summary>Walks back from a loop entry looking for R's base (a <c>mov R,imm</c> plus
    /// <c>add R,imm</c> steps) and the CX count setup (<c>mov cx,imm</c>, <c>mov cx,[seg:const]</c>,
    /// <c>mov cx,[seg:R]</c>; <c>sub cx,imm</c> is skipped). Emits one list per proven path.</summary>
    private void WalkEntry(CfgInstruction entry, int registerIndex, ushort stride,
        List<RecordList> lists, ref int paths) =>
        WalkEntryNode(entry, registerIndex, stride, 0, 0, CountKind.None, 0, 0, -1, 0, false,
            false, [], lists, ref paths);

    private void WalkEntryNode(CfgInstruction node, int registerIndex, ushort stride,
        ushort delta, int transitions, CountKind countKind, ushort countValue,
        ushort deltaAtCount, int countSegmentRegisterIndex, ushort baseValue, bool hasBase,
        bool passThrough, HashSet<CfgInstruction> visited, List<RecordList> lists, ref int paths) {
        if (_nodeVisits >= CalleeReturnScanner.MaxNodes || !visited.Add(node)) {
            return;
        }
        _nodeVisits++;
        WalkEntryBody(node, registerIndex, stride, delta, transitions, countKind, countValue,
            deltaAtCount, countSegmentRegisterIndex, baseValue, hasBase, passThrough, visited,
            lists, ref paths);
        visited.Remove(node);
    }

    private void WalkEntryBody(CfgInstruction node, int registerIndex, ushort stride,
        ushort delta, int transitions, CountKind countKind, ushort countValue,
        ushort deltaAtCount, int countSegmentRegisterIndex, ushort baseValue, bool hasBase,
        bool passThrough, HashSet<CfgInstruction> visited, List<RecordList> lists, ref int paths) {
        ushort registerBit = (ushort)(1 << registerIndex);
        const ushort cxBit = (ushort)RegisterBits.Cx;
        ushort writes = passThrough ? (ushort)0 : RegisterWriteSet.MayWrite(node);
        if (writes == (ushort)RegisterBits.AllExceptCs) {
            return;
        }
        if (!hasBase && (writes & registerBit) != 0) {
            if (IsMovRegisterImmediate(node, registerIndex, out ushort baseImmediate)) {
                baseValue = (ushort)(baseImmediate + delta);
                hasBase = true;
            } else if (IsAddRegisterImmediate(node, registerIndex, out ushort addImmediate)) {
                delta = (ushort)(delta + addImmediate);
            } else {
                return;
            }
        }
        if (countKind == CountKind.None && (writes & cxBit) != 0) {
            if (IsSubCxImmediate(node)) {
                // The loaded count is an upper bound: the subtraction only tightens it.
            } else if (IsMovCxImmediate(node, out ushort countImmediate)) {
                countKind = CountKind.Constant;
                countValue = countImmediate;
            } else if (IsMovCxMemory(node, registerIndex, out int cellSegment, out ushort? cellOffset)) {
                countSegmentRegisterIndex = cellSegment;
                if (cellOffset is ushort offset) {
                    countKind = CountKind.Cell;
                    countValue = offset;
                } else {
                    countKind = CountKind.CellThroughRegister;
                    deltaAtCount = delta;
                }
            } else {
                return;
            }
        }
        if (hasBase && countKind != CountKind.None) {
            RecordList? list = countKind switch {
                CountKind.Constant when countValue <= MaxCount =>
                    new RecordList(baseValue, stride, countValue, NoCountSegmentRegister, 0),
                CountKind.Cell => new RecordList(baseValue, stride, null, countSegmentRegisterIndex, countValue),
                CountKind.CellThroughRegister => new RecordList(baseValue, stride, null,
                    countSegmentRegisterIndex, (ushort)(baseValue - deltaAtCount)),
                _ => null
            };
            if (list is not null) {
                lists.Add(list);
            }
            return;
        }
        // While the count is unproven a crossed call must clear CX as well as R, or the count the
        // walk would find is not the count the loop uses.
        ushort crossBits = countKind == CountKind.None ? (ushort)(registerBit | cxBit) : registerBit;
        List<(CfgInstruction Node, bool PassThrough)> continuations =
            Continuations(node, crossBits).ToList();
        paths += Math.Max(0, continuations.Count - 1);
        foreach ((CfgInstruction predecessor, bool predPassThrough) in continuations) {
            int nextTransitions = transitions + TransitionCount(node, predecessor);
            if (paths <= MaxPaths && nextTransitions <= MaxTransitions) {
                WalkEntryNode(predecessor, registerIndex, stride, delta, nextTransitions, countKind,
                    countValue, deltaAtCount, countSegmentRegisterIndex, baseValue, hasBase,
                    predPassThrough, visited, lists, ref paths);
            }
        }
    }

    // --- instruction shape matchers ---

    private static bool IsMovRegisterImmediate(CfgInstruction node, int registerIndex, out ushort immediate) {
        immediate = 0;
        if (node.DisplayAst is not InstructionNode { Operation: InstructionOperation.MOV, Parameters: [var destination, var source] }
            || SwitchTableRecognizer.AsRegister(destination) is not { RegisterIndex: var index }
            || index != registerIndex
            || SwitchTableRecognizer.AsConstant(source) is not ulong value
            || value > 0xFFFF) {
            return false;
        }
        immediate = (ushort)value;
        return true;
    }

    private static bool IsAddRegisterImmediate(CfgInstruction node, int registerIndex, out ushort immediate) {
        immediate = 0;
        if (node.DisplayAst is not InstructionNode { Operation: InstructionOperation.ADD, Parameters: [var destination, var source] }
            || SwitchTableRecognizer.AsRegister(destination) is not { RegisterIndex: var index }
            || index != registerIndex
            || SwitchTableRecognizer.AsConstant(source) is not ulong value
            || value is 0 or > 0xFFFF) {
            return false;
        }
        immediate = (ushort)value;
        return true;
    }

    private static bool IsSubCxImmediate(CfgInstruction node) =>
        node.DisplayAst is InstructionNode { Operation: InstructionOperation.SUB, Parameters: [var destination, var source] }
        && SwitchTableRecognizer.AsRegister(destination) is { RegisterIndex: CxRegisterIndex }
        && SwitchTableRecognizer.AsConstant(source) is not null;

    private static bool IsMovCxImmediate(CfgInstruction node, out ushort immediate) {
        immediate = 0;
        if (node.DisplayAst is not InstructionNode { Operation: InstructionOperation.MOV, Parameters: [var destination, var source] }
            || SwitchTableRecognizer.AsRegister(destination) is not { RegisterIndex: CxRegisterIndex }
            || SwitchTableRecognizer.AsConstant(source) is not ulong value
            || value > 0xFFFF) {
            return false;
        }
        immediate = (ushort)value;
        return true;
    }

    /// <summary><c>mov cx, word [seg:const]</c> gives the cell's absolute offset;
    /// <c>mov cx, word [seg:R]</c> gives a cell whose offset is R's value at that point (null).</summary>
    private static bool IsMovCxMemory(CfgInstruction node, int registerIndex,
        out int segmentRegisterIndex, out ushort? offset) {
        segmentRegisterIndex = -1;
        offset = null;
        if (node.DisplayAst is not InstructionNode { Operation: InstructionOperation.MOV, Parameters: [var destination, SegmentedPointerNode pointer] }
            || SwitchTableRecognizer.AsRegister(destination) is not { RegisterIndex: CxRegisterIndex }
            || SwitchTableRecognizer.GetSegmentRegisterIndex(pointer) is not int segmentIndex) {
            return false;
        }
        segmentRegisterIndex = segmentIndex;
        if (SwitchTableRecognizer.AsConstant(pointer.Offset) is ulong constOffset) {
            if (constOffset > 0xFFFF) {
                return false;
            }
            offset = (ushort)constOffset;
            return true;
        }
        return SwitchTableRecognizer.AsRegister(pointer.Offset) is { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: var index }
            && index == registerIndex;
    }
}
