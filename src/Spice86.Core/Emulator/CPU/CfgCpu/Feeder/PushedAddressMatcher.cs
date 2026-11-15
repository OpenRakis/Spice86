namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Matches the manufactured-return push in front of an indirect jump: <c>push imm16</c>, or
/// <c>push r16</c> whose last write is a <c>mov r16, imm16</c>. Shared by
/// <see cref="SwitchTableRecognizer"/> (the push may sit between scale and transfer in a table
/// sequence) and by <see cref="SpeculativeExplorer"/>'s pushed-return scan (the push must be the
/// only instruction touching the stack in the jump's block).
/// </summary>
internal static class PushedAddressMatcher {
    /// <summary>
    /// True when <paramref name="node"/> is a 16-bit push. For <c>push imm16</c> the immediate is
    /// returned in <paramref name="immediate"/> and <paramref name="pushedRegister"/> is -1; for
    /// <c>push r16</c> the register index is returned in <paramref name="pushedRegister"/> and the
    /// pushed value must be resolved with <see cref="ResolvePushedImmediate"/>.
    /// </summary>
    internal static bool TryMatchPush(CfgInstruction node, out int pushedRegister, out ushort immediate) {
        pushedRegister = -1;
        immediate = 0;
        if (node.DisplayAst is not InstructionNode {
                Operation: InstructionOperation.PUSH, Parameters: [var parameter] }) {
            return false;
        }
        switch (parameter) {
            case RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } register:
                pushedRegister = register.RegisterIndex;
                return true;
            case TypeConversionNode { Value: RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } inner }:
                pushedRegister = inner.RegisterIndex;
                return true;
            default:
                ulong? value = AsConstant(parameter);
                if (value is null || value > 0xFFFF) {
                    return false;
                }
                immediate = (ushort)value.Value;
                return true;
        }
    }

    /// <summary>
    /// The setter for a <c>push r16</c>: <c>mov r16, imm16</c> on the pushed register.
    /// </summary>
    internal static bool IsSetterMov(CfgInstruction node, int pushedRegister) =>
        node.DisplayAst is InstructionNode {
            Operation: InstructionOperation.MOV, Parameters: [var target, var source] }
        && AsRegister(target) is { DataType.BitWidth: BitWidth.WORD_16 } register
        && register.RegisterIndex == pushedRegister
        && AsConstant(source) is not null;

    /// <summary>
    /// Resolves the value a <c>push r16</c> pushes: scans <paramref name="newestFirst"/> (most
    /// recent instruction first) for writes to the register. Every write must be a
    /// <c>mov r16, imm16</c>; the newest one wins. Null when a foreign write occurs or no setter
    /// exists in the scanned instructions.
    /// </summary>
    internal static ushort? ResolvePushedImmediate(int pushedRegister, IReadOnlyList<CfgInstruction> newestFirst) {
        ushort? result = null;
        foreach (CfgInstruction instruction in newestFirst) {
            if (!WritesRegister(instruction, pushedRegister)) {
                continue;
            }
            if (!IsSetterMov(instruction, pushedRegister)
                || instruction.DisplayAst is not InstructionNode { Parameters: [_, var source] }
                || AsConstant(source) is not ulong immediate) {
                return null;
            }
            result ??= (ushort)immediate;
        }
        return result;
    }

    /// <summary>
    /// Whether the instruction writes to or through the stack: push/pop of anything (including
    /// pushf/popf and segment registers), a write to SP, call/int/ret/iret/enter/leave. Anything
    /// whose AST is not a plain instruction node counts as touching (conservative).
    /// </summary>
    internal static bool TouchesStack(CfgInstruction node) {
        if (node.DisplayAst is not InstructionNode { Operation: var op, Parameters: var parameters }) {
            return true;
        }
        switch (op) {
            case InstructionOperation.PUSH or InstructionOperation.PUSHA or InstructionOperation.PUSHAD
                or InstructionOperation.PUSHF or InstructionOperation.PUSHFD
                or InstructionOperation.POP or InstructionOperation.POPA or InstructionOperation.POPAD
                or InstructionOperation.POPF or InstructionOperation.POPFD
                or InstructionOperation.ENTER or InstructionOperation.ENTERW
                or InstructionOperation.LEAVE or InstructionOperation.LEAVEW:
                return true;
            default:
                break;
        }
        if (op is InstructionOperation.CALL_NEAR or InstructionOperation.CALL_FAR
            or InstructionOperation.RET_NEAR or InstructionOperation.RET_FAR
            or InstructionOperation.IRET) {
            return true;
        }
        if (node.IsCall || node.IsReturn) {
            return true;
        }
        // Any write to SP (mov sp, / add sp, / pop sp, / lea sp, ...) changes the stack pointer.
        return parameters.Length > 0
            && op is not InstructionOperation.CMP and not InstructionOperation.TEST
            && AsRegister(parameters[0]) is { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: 4 };
    }

    /// <summary>True when <paramref name="node"/> is a near indirect jump: a register or a memory
    /// pointer operand, as opposed to a direct target.</summary>
    internal static bool IsIndirectNearJump(CfgInstruction node) =>
        node.DisplayAst is InstructionNode {
            Operation: InstructionOperation.JMP_NEAR, Parameters: [var parameter] }
        && (AsRegister(parameter) is not null || AsPointer(parameter) is not null);

    private static RegisterNode? AsRegister(IVisitableAstNode node) =>
        node is TypeConversionNode conversion ? conversion.Value as RegisterNode : node as RegisterNode;

    private static SegmentedPointerNode? AsPointer(IVisitableAstNode node) =>
        node is TypeConversionNode conversion ? conversion.Value as SegmentedPointerNode : node as SegmentedPointerNode;

    private static ulong? AsConstant(IVisitableAstNode node) => node switch {
        TypeConversionNode { Value: var inner } => AsConstant(inner),
        ConstantNode constant => constant.Value,
        InstructionFieldNode field => field.ConstantValue,
        _ => null
    };

    /// <summary>Conservative register-write test: a destination operand naming the register on an
    /// instruction that stores (anything but cmp/test), or an xchg naming it on either side.</summary>
    private static bool WritesRegister(CfgInstruction node, int registerIndex) {
        if (node.DisplayAst is not InstructionNode { Operation: var op, Parameters: var parameters }) {
            return false;
        }
        bool names = parameters.Length > 0
            && AsRegister(parameters[0]) is { RegisterIndex: var index } && index == registerIndex;
        if (op == InstructionOperation.XCHG) {
            return names || parameters.Length > 1
                && AsRegister(parameters[1]) is { RegisterIndex: var other } && other == registerIndex;
        }
        return names && op is not InstructionOperation.CMP and not InstructionOperation.TEST
            and not InstructionOperation.PUSH;
    }
}
