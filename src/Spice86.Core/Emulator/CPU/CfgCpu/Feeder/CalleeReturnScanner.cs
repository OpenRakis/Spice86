namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The call/return distance a call site or a return instruction belongs to. A callee only
/// proves to return for sites of the distance its endings are uniform on: a near call needs
/// every path to end in <c>ret</c>/<c>ret n</c>, a far call in <c>retf</c> and an interrupt
/// call in <c>iret</c>.
/// </summary>
internal enum CallDistance {
    Near,
    Far,
    Interrupt
}

/// <summary>
/// Proves that a never-called callee entry returns by walking its already-decoded speculative
/// subgraph: every path must end in a return of one uniform <see cref="CallDistance"/> with the
/// callee's own stack balanced (pushes undone). Stack deltas are tracked through pushes and
/// pops of registers, segment registers and immediates, <c>sub/add sp,imm</c>, the
/// <c>push bp; mov bp,sp ... mov sp,bp; pop bp</c> frame and <c>leave</c>; any other write to BP
/// closes the tracked frame. The scan refuses on
/// any other SP write (detected through <see cref="RegisterWriteSet"/>), any indirect transfer,
/// any call or int the caller does not already know to return, any write through
/// <c>SS:[BP+n]</c>/<c>SS:[SP+n]</c> that may hit the return address, any statement shape it
/// does not understand, and any subgraph over <see cref="MaxNodes"/> nodes.
/// </summary>
internal sealed class CalleeReturnScanner {
    /// <summary>Maximum number of distinct instructions a callee subgraph may span.</summary>
    public const int MaxNodes = 512;

    public enum Outcome {
        /// <summary>Every path ends in a balanced <c>ret</c>/<c>ret n</c>.</summary>
        Near,
        /// <summary>Every path ends in a balanced <c>retf</c>.</summary>
        Far,
        /// <summary>Every path ends in a balanced <c>iret</c>.</summary>
        Interrupt,
        /// <summary>A needed node is not decoded yet; rescan once the graph grew.</summary>
        Incomplete,
        /// <summary>A rule was violated, or endings do not agree on one distance.</summary>
        Refused
    }

    /// <summary>Stack state one path carries: bytes the callee pushed so far and, when a
    /// <c>mov bp,sp</c> frame is open, the pushed count at the moment BP was set to SP.</summary>
    private readonly record struct PathState(CfgInstruction Node, int Pushed, int? Frame);

    private readonly CfgNodeIndex _nodeIndex;

    public CalleeReturnScanner(CfgNodeIndex nodeIndex) {
        _nodeIndex = nodeIndex;
    }

    /// <summary>
    /// Scans the nodes reachable from <paramref name="calleeEntry"/> through static successors.
    /// <paramref name="callMayReturn"/> decides whether a nested call or interrupt call is known
    /// to return (CFG evidence, emulator handler or an earlier proof); its continuation is then
    /// followed at <see cref="CfgInstruction.NextInMemoryAddress32"/>.
    /// </summary>
    public Outcome Scan(SegmentedAddress calleeEntry, Func<CfgInstruction, bool> callMayReturn) {
        HashSet<PathState> visited = new();
        HashSet<CfgInstruction> nodes = new();
        Stack<PathState> work = new();
        foreach (CfgInstruction start in _nodeIndex.GetAtAddress(calleeEntry)) {
            work.Push(new PathState(start, 0, null));
        }
        if (work.Count == 0) {
            return Outcome.Incomplete;
        }
        CallDistance? endings = null;
        bool incomplete = false;
        while (work.Count > 0) {
            PathState state = work.Pop();
            if (!visited.Add(state)) {
                continue;
            }
            CfgInstruction node = state.Node;
            nodes.Add(node);
            if (nodes.Count > MaxNodes) {
                return Outcome.Refused;
            }
            Verdict verdict = ClassifyTerminator(node, callMayReturn,
                out CallDistance end, out SegmentedAddress? continuation,
                out List<SegmentedAddress>? successors, ref state);
            if (verdict == Verdict.Refuse) {
                return Outcome.Refused;
            }
            if (verdict == Verdict.End) {
                if (state.Pushed != 0 || (endings ??= end) != end) {
                    // Unbalanced path, or endings of different distances.
                    return Outcome.Refused;
                }
                continue;
            }
            if (continuation is { } next) {
                incomplete |= !PushAll(work, next, state);
                continue;
            }
            if (successors is { } list) {
                foreach (SegmentedAddress successor in list) {
                    incomplete |= !PushAll(work, successor, state);
                }
            }
        }
        if (incomplete) {
            return Outcome.Incomplete;
        }
        return endings switch {
            CallDistance.Near => Outcome.Near,
            CallDistance.Far => Outcome.Far,
            CallDistance.Interrupt => Outcome.Interrupt,
            // No return was reached at all; the caller treats it as a refusal.
            _ => Outcome.Refused
        };
    }

    private bool PushAll(Stack<PathState> work, SegmentedAddress address, PathState state) {
        bool found = false;
        foreach (CfgInstruction target in _nodeIndex.GetAtAddress(address)) {
            found = true;
            work.Push(state with { Node = target });
        }
        return found;
    }

    private enum Verdict {
        /// <summary>The node ends this path in a return of <see cref="CallDistance"/> kind.</summary>
        End,
        /// <summary>The path continues at <c>continuation</c> or <c>successors</c>.</summary>
        Advance,
        /// <summary>A rule was violated; the callee cannot be proven.</summary>
        Refuse
    }

    /// <summary>
    /// Classifies one instruction. <see cref="Verdict.End"/> fills <paramref name="end"/> with
    /// the return kind; <see cref="Verdict.Advance"/> fills <paramref name="continuation"/>
    /// (call/int to a callee known to return) or <paramref name="successors"/> (jumps and
    /// fall-throughs); <see cref="Verdict.Refuse"/> on a rule violation.
    /// </summary>
    private Verdict ClassifyTerminator(CfgInstruction node, Func<CfgInstruction, bool> callMayReturn,
        out CallDistance end, out SegmentedAddress? continuation,
        out List<SegmentedAddress>? successors, ref PathState state) {
        end = default;
        continuation = null;
        successors = null;
        if (node.IsInvalid || node.ExecutionAst is null) {
            return Verdict.Refuse;
        }
        IVisitableAstNode last = node.ExecutionAst is BlockNode block && block.Statements.Count > 0
            ? block.Statements[^1]
            : node.ExecutionAst;
        if (node.IsReturn) {
            end = last switch {
                ReturnNearNode => CallDistance.Near,
                ReturnFarNode => CallDistance.Far,
                ReturnInterruptNode => CallDistance.Interrupt,
                _ => CallDistance.Near
            };
            return last is ReturnNearNode or ReturnFarNode or ReturnInterruptNode
                ? Verdict.End
                : Verdict.Refuse;
        }
        if (node.IsCall) {
            if (last is not (CallNearNode or CallFarNode or InterruptCallNode)) {
                return Verdict.Refuse;
            }
            // Indirect calls carry no static successor; a call or int to a callee not
            // known to return cannot be proven either.
            if (last is not InterruptCallNode && node.StaticSuccessorAddresses.Count == 0) {
                return Verdict.Refuse;
            }
            if (!callMayReturn(node)) {
                return Verdict.Refuse;
            }
            continuation = node.NextInMemoryAddress32.ToSegmentedAddress();
            return Verdict.Advance;
        }
        if (node.IsJump) {
            if (last is JumpFarNode || node.StaticSuccessorAddresses.Count == 0) {
                // A far jump leaves the callee; an indirect one cannot be proven to come back.
                return Verdict.Refuse;
            }
            successors = [..node.StaticSuccessorAddresses];
            return Verdict.Advance;
        }
        if (node.StaticSuccessorAddresses.Count == 0) {
            // Callback, hlt or unrecognised terminator: the path cannot be proven to come back.
            return Verdict.Refuse;
        }
        int pushed = state.Pushed;
        int? frame = state.Frame;
        if (!ApplyStackEffect(node, ref pushed, ref frame)) {
            return Verdict.Refuse;
        }
        state = state with { Pushed = pushed, Frame = frame };
        successors = [..node.StaticSuccessorAddresses];
        return Verdict.Advance;
    }

    /// <summary>
    /// Applies one instruction's stack effect to <paramref name="pushed"/> and
    /// <paramref name="frame"/>. Accepts only the statement shapes the scan tracks; uses
    /// <see cref="RegisterWriteSet"/> so a write to SP outside those shapes refuses.
    /// </summary>
    internal static bool ApplyStackEffect(CfgInstruction node, ref int pushed, ref int? frame) {
        bool spAccounted = false;
        bool opensFrame = false;
        IVisitableAstNode ast = node.ExecutionAst!;
        IEnumerable<IVisitableAstNode> statements = ast is BlockNode block ? block.Statements : [ast];
        foreach (IVisitableAstNode statement in statements) {
            switch (statement) {
                case StackCallNode stack:
                    if (!ApplyStackOperation(stack.Operation, stack.Width, ref pushed, ref frame)) {
                        return false;
                    }
                    spAccounted = true;
                    break;
                case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign:
                    if (!ApplyAssign(assign, ref pushed, ref frame, ref spAccounted, ref opensFrame)) {
                        return false;
                    }
                    break;
                case MoveIpNextNode:
                    break;
                case VariableDeclarationNode declaration:
                    // A temporary declaration carries the pop/peek a pop instruction emits;
                    // apply its stack effect like an assign's right side.
                    foreach (StackCallValueNode stack in StackValues(declaration.Initializer)) {
                        if (!ApplyStackOperation(stack.Operation, stack.Width, ref pushed, ref frame)) {
                            return false;
                        }
                        spAccounted = true;
                    }
                    break;
                default:
                    // A shape the scan does not understand may do anything to the stack.
                    return false;
            }
            if (pushed < 0) {
                // The path popped into the caller's frame, return address included.
                return false;
            }
        }
        ushort writes = RegisterWriteSet.MayWrite(node);
        if (!opensFrame && (writes & (ushort)RegisterBits.Bp) != 0) {
            // Any BP write but mov bp,sp (pop bp, popa, mov bp,ax, ...) leaves the frame unknown.
            frame = null;
        }
        return spAccounted || (writes & (ushort)RegisterBits.Sp) == 0;
    }

    private static bool ApplyStackOperation(StackOperation operation, BitWidth width, ref int pushed, ref int? frame) {
        int bytes = width == BitWidth.DWORD_32 ? 4 : 2;
        switch (operation) {
            case StackOperation.Push:
                pushed += bytes;
                return true;
            case StackOperation.Pop:
                pushed -= bytes;
                return true;
            case StackOperation.Peek:
                return true;
            case StackOperation.PushAll:
                pushed += bytes * 8;
                return true;
            case StackOperation.PopAll:
                pushed -= bytes * 8;
                return true;
            case StackOperation.Leave:
                if (frame is null) {
                    return false;
                }
                // mov sp,bp; pop bp: only the saved BP remains popped off the callee's count.
                pushed = frame.Value - bytes;
                frame = null;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyAssign(BinaryOperationNode assign, ref int pushed, ref int? frame, ref bool spAccounted,
        ref bool opensFrame) {
        // Pops and peeks on the right side move SP like their void counterparts.
        foreach (StackCallValueNode stack in StackValues(assign.Right)) {
            if (!ApplyStackOperation(stack.Operation, stack.Width, ref pushed, ref frame)) {
                return false;
            }
            spAccounted = true;
        }
        switch (assign.Left) {
            case RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex } target:
                if (assign.Right is RegisterNode { RegisterIndex: (int)RegisterIndex.BpIndex }) {
                    // mov sp,bp: only valid inside a tracked frame.
                    if (frame is null) {
                        return false;
                    }
                    pushed = frame.Value;
                    spAccounted = true;
                    return true;
                }
                if (assign.Right is BinaryOperationNode {
                        BinaryOperation: BinaryOperation.PLUS or BinaryOperation.MINUS,
                        Left: RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex }
                    } adjust && TryConst(adjust.Right, out long imm)) {
                    pushed += adjust.BinaryOperation == BinaryOperation.MINUS ? (int)imm : -(int)imm;
                    spAccounted = true;
                    return true;
                }
                if (assign.Right is AluCallValueNode {
                        Operation: AluOperation.Add or AluOperation.Sub,
                        CallNode.Arguments: [RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex }, var aluImmNode]
                    } alu && TryConst(aluImmNode, out long aluImm)) {
                    pushed += alu.Operation == AluOperation.Sub ? (int)aluImm : -(int)aluImm;
                    spAccounted = true;
                    return true;
                }
                // Any other SP write (mov sp,imm, xchg, pop sp, ...).
                return false;
            case RegisterNode { RegisterIndex: (int)RegisterIndex.BpIndex, DataType.BitWidth: not BitWidth.BYTE_8 }
                when assign.Right is RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex }:
                // mov bp,sp: opens or refreshes the tracked frame.
                frame = pushed;
                opensFrame = true;
                return true;
            case SegmentedPointerNode pointer:
                return CheckStackWrite(pointer, pushed, frame);
            default:
                return true;
        }
    }

    /// <summary>
    /// True when a store through <paramref name="pointer"/> cannot reach the return address:
    /// either it is not an SS-relative write through SP or BP, or its displacement stays
    /// outside the (near or far) return-address window. A BP write without a tracked frame,
    /// or an address shape the scan cannot resolve while it mentions SP or BP, may hit.
    /// </summary>
    private static bool CheckStackWrite(SegmentedPointerNode pointer, int pushed, int? frame) {
        if (pointer.Segment is not SegmentRegisterNode { RegisterIndex: (int)SegmentRegisterIndex.SsIndex }) {
            return true;
        }
        if (!TryBaseDisp(pointer.Offset, out int baseRegister, out int displacement)) {
            return !MentionsStackRegister(pointer.Offset);
        }
        int size = pointer.DataType.BitWidth == BitWidth.DWORD_32 ? 4 : 2;
        switch (baseRegister) {
            case (int)RegisterIndex.SpIndex:
                return !Overlaps(displacement, size, pushed);
            case (int)RegisterIndex.BpIndex:
                return frame is not null && !Overlaps(displacement, size, frame.Value);
            default:
                return true;
        }
    }

    /// <summary>The four bytes a near or far return address can occupy sit at
    /// <c>[base+delta .. base+delta+3]</c> relative to the write base (SP, or BP in a frame).
    /// A write of <paramref name="size"/> bytes at <paramref name="displacement"/> overlaps
    /// when <c>disp &lt;= delta+3</c> and <c>disp+size-1 &gt;= delta</c>.</summary>
    private static bool Overlaps(int displacement, int size, int delta) =>
        displacement <= delta + 3 && displacement + size - 1 >= delta;

    /// <summary>Resolves <c>reg</c>, <c>reg+const</c> and <c>reg-const</c> offsets.</summary>
    private static bool TryBaseDisp(IVisitableAstNode offset, out int baseRegister, out int displacement) {
        displacement = 0;
        switch (offset) {
            case RegisterNode register:
                baseRegister = register.RegisterIndex;
                return true;
            case BinaryOperationNode {
                    BinaryOperation: BinaryOperation.PLUS or BinaryOperation.MINUS,
                    Left: RegisterNode register
                } bin when TryConst(bin.Right, out long imm):
                baseRegister = register.RegisterIndex;
                displacement = bin.BinaryOperation == BinaryOperation.MINUS ? -(int)imm : (int)imm;
                return true;
            default:
                baseRegister = -1;
                return false;
        }
    }

    private static bool MentionsStackRegister(IVisitableAstNode node) => node switch {
        RegisterNode register => register.RegisterIndex is (int)RegisterIndex.SpIndex
            or (int)RegisterIndex.BpIndex,
        BinaryOperationNode binary => MentionsStackRegister(binary.Left) || MentionsStackRegister(binary.Right),
        _ => false
    };

    private static IEnumerable<StackCallValueNode> StackValues(IVisitableAstNode node) {
        if (node is StackCallValueNode stack) {
            yield return stack;
        }
        if (node is BinaryOperationNode binary) {
            foreach (StackCallValueNode value in StackValues(binary.Left)) {
                yield return value;
            }
            foreach (StackCallValueNode value in StackValues(binary.Right)) {
                yield return value;
            }
        }
    }

    private static bool TryConst(IVisitableAstNode node, out long value) {
        switch (node) {
            case TypeConversionNode conversion:
                return TryConst(conversion.Value, out value);
            case ConstantNode constant:
                value = constant.SignedValue;
                return true;
            case InstructionFieldNode field:
                value = (long)field.ConstantValue;
                return true;
            default:
                value = 0;
                return false;
        }
    }

}
