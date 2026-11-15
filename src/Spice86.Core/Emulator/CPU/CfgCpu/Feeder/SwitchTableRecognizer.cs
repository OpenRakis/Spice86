namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Recognizes the compiler-generated "bounds check + scale + indirect transfer" sequence in front of
/// a switch/call table read, e.g. <c>cmp ax,N; ja def; xchg bx,ax; shl bx,1; jmp [cs:bx+T]</c>.
/// Matches on instruction display ASTs (<see cref="InstructionNode"/> operation and operand nodes),
/// walking backwards at most eight instructions from the transfer through the transfer's block and
/// single-predecessor blocks, reading only live nodes. Requires exactly
/// guard -&gt; [widen] -&gt; [copy] -&gt; scale -&gt; [push] -&gt; transfer with nothing else in
/// between, where the optional push is a manufactured return (<c>push imm16</c>, or
/// <c>push r16</c> set only by <c>mov r16, imm16</c>, never the index register).
/// Recognition results, including negative ones, are cached per transfer node.
/// </summary>
internal class SwitchTableRecognizer {
    /// <summary>Maximum number of instructions walked backwards from the transfer.</summary>
    private const int MaxWalkBack = 8;

    /// <summary>
    /// A recognized table site.
    /// </summary>
    /// <param name="Bound">The guard immediate N: valid indices are 0..N, so the table has N+1 entries.</param>
    /// <param name="SegmentRegisterIndex">Index into <c>State.SegmentRegisters.UInt16</c> of the table segment.</param>
    /// <param name="TableOffset">Displacement of the table base inside the segment.</param>
    /// <param name="IsCall">True when the transfer is an indirect near call, false for a jump.</param>
    internal sealed record TableShape(ushort Bound, int SegmentRegisterIndex, ushort TableOffset, bool IsCall);

    private readonly Dictionary<CfgInstruction, TableShape?> _cache = new();

    /// <summary>
    /// Recognizes the table shape ending at <paramref name="transfer"/>, or null when the sequence
    /// does not match. Negative results are cached like positive ones.
    /// </summary>
    public TableShape? Recognize(CfgInstruction transfer) {
        if (_cache.TryGetValue(transfer, out TableShape? cached)) {
            return cached;
        }
        TableShape? result = TryRecognize(transfer);
        _cache[transfer] = result;
        return result;
    }

    /// <summary>
    /// True when <paramref name="node"/> is a live near indirect jmp/call reading a word memory
    /// operand: the only kind of instruction a table resolution can apply to. Cheap enough to run
    /// per execution as a first filter before the cached <see cref="Recognize"/>.
    /// </summary>
    internal static bool IsIndirectTransfer(ICfgNode node) =>
        node is CfgInstruction { IsLive: true, DisplayAst: InstructionNode {
            Operation: InstructionOperation.JMP_NEAR or InstructionOperation.CALL_NEAR,
            Parameters: [SegmentedPointerNode { DataType.BitWidth: BitWidth.WORD_16 }] } };

    private static TableShape? TryRecognize(CfgInstruction transfer) {
        if (!IsIndirectTransfer(transfer)
            || transfer.DisplayAst is not InstructionNode {
                Parameters: [SegmentedPointerNode pointer] } display) {
            return null;
        }
        if (!TryGetIndexExpression(pointer.Offset, out int indexRegister, out ushort displacement)
            || GetSegmentRegisterIndex(pointer) is not int segmentRegisterIndex) {
            return null;
        }

        // chain[0] is the instruction immediately before the transfer.
        List<CfgInstruction> chain = WalkBack(transfer);
        int i = 0;
        // Optional manufactured return: `push imm16`, or `push r16` (never the index register)
        // whose only writes in the walk window are `mov r16, imm16`. The setter movs may sit
        // anywhere in the window, so they are skipped before each expected element.
        int pushedRegister = -1;
        if (i < chain.Count && PushedAddressMatcher.TryMatchPush(chain[i], out int preg, out _)
            && preg != indexRegister) {
            pushedRegister = preg;
            i++;
        }
        void SkipSetterMovs() {
            while (i < chain.Count && pushedRegister >= 0
                   && PushedAddressMatcher.IsSetterMov(chain[i], pushedRegister)) {
                i++;
            }
        }
        SkipSetterMovs();
        if (i >= chain.Count || !IsScale(chain[i], indexRegister)) {
            return null;
        }
        i++;
        int comparedRegister16 = indexRegister;
        SkipSetterMovs();
        if (i < chain.Count && TryGetCopy(chain[i], indexRegister, out int copiedFrom)) {
            comparedRegister16 = copiedFrom;
            i++;
        }
        bool widened = false;
        bool widenedByCbw = false;
        SkipSetterMovs();
        if (i < chain.Count && IsWiden(chain[i], comparedRegister16, out bool byCbw)) {
            widened = true;
            widenedByCbw = byCbw;
            i++;
        }
        SkipSetterMovs();
        if (i >= chain.Count || !TryMatchConditionalJump(chain[i], out bool jumpOnAbove)) {
            return null;
        }
        CfgInstruction jump = chain[i];
        // The instruction right after the jump in execution order: the previous chain entry, or
        // the transfer itself when the jump immediately precedes it.
        CfgInstruction afterJump = i == 0 ? transfer : chain[i - 1];
        SegmentedAddress jumpTarget = jumpOnAbove
            ? jump.NextInMemoryAddress32.ToSegmentedAddress()
            : jump.StaticSuccessorAddresses.Count > 0
                ? jump.StaticSuccessorAddresses[0]
                : new SegmentedAddress(0, 0);
        if (!afterJump.Address.Equals(jumpTarget)) {
            return null;
        }
        i++;
        if (i >= chain.Count || !TryMatchGuard(chain[i], comparedRegister16, widened, out ushort bound)) {
            return null;
        }
        // cbw sign-extends a byte index; a bound of 0x80 or more would read negative indices.
        if (widenedByCbw && bound >= 0x80) {
            return null;
        }
        // A `push r16` manufactured return needs a `mov r16, imm16` setter as its only write to
        // the register in the walk window; anything else is an argument push.
        if (pushedRegister >= 0
            && PushedAddressMatcher.ResolvePushedImmediate(pushedRegister, chain) is null) {
            return null;
        }
        return new TableShape(bound, segmentRegisterIndex, displacement,
            display.Operation == InstructionOperation.CALL_NEAR);
    }

    // --- backward walk ---

    /// <summary>Live instructions before <paramref name="transfer"/>, most recent first, capped at
    /// <see cref="MaxWalkBack"/>. Stops at the first instruction without a unique live instruction
    /// predecessor (block join or ambiguous chain).</summary>
    private static List<CfgInstruction> WalkBack(CfgInstruction transfer) {
        List<CfgInstruction> chain = new();
        CfgInstruction cursor = transfer;
        while (chain.Count < MaxWalkBack) {
            CfgInstruction? previous = PreviousInstruction(cursor);
            if (previous is null) {
                break;
            }
            chain.Add(previous);
            cursor = previous;
        }
        return chain;
    }

    /// <summary>
    /// The unique live instruction predecessor of <paramref name="node"/>: the previous instruction
    /// inside its block, or, at a block head, the last instruction of the single predecessor block.
    /// Null when there is none or several (ambiguous chain).
    /// </summary>
    private static CfgInstruction? PreviousInstruction(CfgInstruction node) {
        CfgInstruction? previous = null;
        foreach (ICfgNode predecessor in node.Predecessors) {
            if (predecessor is not CfgInstruction { IsLive: true } instruction) {
                continue;
            }
            if (previous is not null) {
                return null;
            }
            previous = instruction;
        }
        return previous;
    }

    // --- shape matchers on display ASTs ---

    internal static RegisterNode? AsRegister(IVisitableAstNode node) =>
        node is TypeConversionNode conversion ? conversion.Value as RegisterNode : node as RegisterNode;

    internal static ulong? AsConstant(IVisitableAstNode node) => node switch {
        TypeConversionNode { Value: var inner } => AsConstant(inner),
        ConstantNode constant => constant.Value,
        InstructionFieldNode field => field.ConstantValue,
        _ => null
    };

    /// <summary>Extracts [R + disp] from the pointer offset: a 16-bit register optionally added to
    /// a constant displacement (disp may be 0).</summary>
    internal static bool TryGetIndexExpression(ValueNode offset, out int registerIndex, out ushort displacement) {
        registerIndex = 0;
        displacement = 0;
        ValueNode inner = offset is TypeConversionNode conversion ? conversion.Value : offset;
        if (inner is RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } register) {
            registerIndex = register.RegisterIndex;
            return true;
        }
        if (inner is BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS } add) {
            RegisterNode? left = AsRegister(add.Left);
            ulong? right = AsConstant(add.Right);
            if (left is null || right is null) {
                left = AsRegister(add.Right);
                right = AsConstant(add.Left);
            }
            if (left is { DataType.BitWidth: BitWidth.WORD_16 } && right is not null) {
                registerIndex = left.RegisterIndex;
                displacement = (ushort)right.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>The segment register index of an explicit override segment, else the default
    /// segment. Null when neither is a segment register node.</summary>
    internal static int? GetSegmentRegisterIndex(SegmentedPointerNode pointer) {
        ValueNode? segment = pointer.Segment ?? pointer.DefaultSegment;
        return segment is SegmentRegisterNode node ? node.RegisterIndex : null;
    }

    /// <summary>scale: <c>shl R,1</c> or <c>add R,R</c> on the transfer's 16-bit index register.</summary>
    private static bool IsScale(CfgInstruction node, int registerIndex) {
        if (node.DisplayAst is not InstructionNode { Operation: var op, Parameters: var p }) {
            return false;
        }
        if (p.Length < 2 || AsRegister(p[0]) is not {
                DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: var idx } || idx != registerIndex) {
            return false;
        }
        return op == InstructionOperation.SHL && AsConstant(p[1]) == 1
            || op == InstructionOperation.ADD && AsRegister(p[1]) is { RegisterIndex: var ridx } && ridx == registerIndex;
    }

    /// <summary>copy: <c>mov R,R1</c> or <c>xchg</c> in either direction (16-bit registers). R is
    /// the register the scale uses; the other register becomes the compared one.</summary>
    private static bool TryGetCopy(CfgInstruction node, int scaledRegister, out int comparedRegister) {
        comparedRegister = 0;
        if (node.DisplayAst is not InstructionNode { Parameters: [var a, var b] } display) {
            return false;
        }
        RegisterNode? left = AsRegister(a);
        RegisterNode? right = AsRegister(b);
        if (left is not { DataType.BitWidth: BitWidth.WORD_16 }
            || right is not { DataType.BitWidth: BitWidth.WORD_16 }) {
            return false;
        }
        bool isCopy = display.Operation switch {
            InstructionOperation.MOV => left.RegisterIndex == scaledRegister,
            InstructionOperation.XCHG => left.RegisterIndex == scaledRegister || right.RegisterIndex == scaledRegister,
            _ => false
        };
        if (!isCopy) {
            return false;
        }
        comparedRegister = left.RegisterIndex == scaledRegister ? right.RegisterIndex : left.RegisterIndex;
        return true;
    }

    /// <summary>widen: <c>xor R1h,R1h</c> (the high byte of the compared 16-bit register) or
    /// <c>cbw</c> (compared register is AX/AL). Both xor operands must be the same high byte.</summary>
    private static bool IsWiden(CfgInstruction node, int comparedRegister16, out bool byCbw) {
        byCbw = false;
        if (node.DisplayAst is not InstructionNode display) {
            return false;
        }
        if (display.Operation == InstructionOperation.CBW) {
            byCbw = comparedRegister16 == 0;
            return byCbw;
        }
        // 16-bit register AX..BX index 0..3 maps to high byte AH..BH index 4..7.
        if (comparedRegister16 > 3) {
            return false;
        }
        return display.Operation == InstructionOperation.XOR
            && display.Parameters is [var a, var b]
            && AsRegister(a) is { DataType.BitWidth: BitWidth.BYTE_8 } left
            && AsRegister(b) is { DataType.BitWidth: BitWidth.BYTE_8 } right
            && left.RegisterIndex == comparedRegister16 + 4
            && right.RegisterIndex == comparedRegister16 + 4;
    }

    /// <summary>The conditional jump: only <c>ja</c> (transfer path on fallthrough) or
    /// <c>jbe</c> (transfer path at the taken target) are accepted.</summary>
    private static bool TryMatchConditionalJump(CfgInstruction node, out bool jumpOnAbove) {
        jumpOnAbove = false;
        if (node.DisplayAst is not InstructionNode { Operation: var op }) {
            return false;
        }
        switch (op) {
            case InstructionOperation.JA_SHORT or InstructionOperation.JA_NEAR:
                jumpOnAbove = true;
                return true;
            case InstructionOperation.JBE_SHORT or InstructionOperation.JBE_NEAR:
                return true;
            default:
                return false;
        }
    }

    /// <summary>guard: <c>cmp Rc, imm</c> where Rc matches the compared register: word compare uses
    /// the 16-bit register directly (no widen allowed); byte compare uses its low byte (widen
    /// mandatory).</summary>
    private static bool TryMatchGuard(CfgInstruction node, int comparedRegister16, bool widened, out ushort bound) {
        bound = 0;
        if (node.DisplayAst is not InstructionNode {
                Operation: InstructionOperation.CMP,
                Parameters: [var target, var source] }) {
            return false;
        }
        if (AsConstant(source) is not ulong immediate) {
            return false;
        }
        switch (AsRegister(target)) {
            case { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: var idx }
                when idx == comparedRegister16 && !widened:
                bound = (ushort)immediate;
                return true;
            case { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: var idx }
                when idx == comparedRegister16 && idx <= 3 && widened:
                bound = (ushort)immediate;
                return true;
            default:
                return false;
        }
    }
}
