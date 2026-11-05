namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Evaluates an AST expression the way the emitted C# computes it: operands carry their
/// <see cref="DataType"/>'s semantics (a signed type's value is sign-extended), arithmetic runs on
/// 64 bits and the result is truncated to the node's width. Division and modulo are never evaluated
/// (a zero constant divisor would throw, and the runtime helpers keep their own behaviour).
/// </summary>
internal static class ConstantEvaluation {
    /// <summary>The C# value of <paramref name="node"/> in 64-bit two's complement, or false when a
    /// part of it is not compile-time constant. <paramref name="leafValueOf"/> supplies the current
    /// value of a register or a declared variable, or null when unknown.</summary>
    public static bool TryEvaluate(IVisitableAstNode node, Func<IVisitableAstNode, ulong?> leafValueOf,
        out ulong value) => TryEvaluate(node, leafValueOf, out value, false);

    /// <summary>Like <see cref="TryEvaluate"/>, for a caller that replaces the node with the
    /// constant: an ALU operation that writes flags cannot be folded away, its call updates them.</summary>
    public static bool TryEvaluateReplacing(IVisitableAstNode node,
        Func<IVisitableAstNode, ulong?> leafValueOf, out ulong value) =>
        TryEvaluate(node, leafValueOf, out value, true);

    private static bool TryEvaluate(IVisitableAstNode node, Func<IVisitableAstNode, ulong?> leafValueOf,
        out ulong value, bool forReplacement) {
        switch (node) {
            case ConstantNode constant:
                value = SemanticValue(constant.DataType, constant.Value);
                return true;
            case InstructionFieldNode field:
                return TryEvaluate(field.ResolvedNode, leafValueOf, out value, forReplacement);
            case RegisterNode or VariableReferenceNode or CpuFlagNode:
                ulong? leafValue = leafValueOf(node);
                value = leafValue ?? 0;
                return leafValue.HasValue;
            case ConditionalValueNode conditional:
                if (!TryEvaluate(conditional.Condition, leafValueOf, out ulong condition, forReplacement)) {
                    value = 0;
                    return false;
                }
                return TryEvaluate(condition != 0 ? conditional.WhenTrue : conditional.WhenFalse,
                    leafValueOf, out value, forReplacement);
            case TypeConversionNode conversion:
                if (!TryEvaluate(conversion.Value, leafValueOf, out ulong converted, forReplacement)) {
                    value = 0;
                    return false;
                }
                value = SemanticValue(conversion.DataType,
                    converted & ConstantNode.Mask(conversion.DataType.BitWidth));
                return true;
            case UnaryOperationNode unary:
                if (!TryEvaluate(unary.Value, leafValueOf, out ulong operand, forReplacement)) {
                    value = 0;
                    return false;
                }
                value = unary.UnaryOperation switch {
                    UnaryOperation.NOT => operand == 0 ? 1UL : 0UL,
                    UnaryOperation.NEGATE => unchecked(0UL - operand),
                    _ => ~operand
                };
                value = SemanticValue(unary.DataType, value & ConstantNode.Mask(unary.DataType.BitWidth));
                return true;
            case BinaryOperationNode binary:
                return TryEvaluateBinary(binary, leafValueOf, out value, forReplacement);
            case AluOperationNode alu:
                if (forReplacement && alu.Flags.MayWrite != StatusFlags.None) {
                    value = 0;
                    return false;
                }
                return TryEvaluateSelfZero(alu, out value)
                    || TryEvaluateAlu(alu, leafValueOf, out value, forReplacement);
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>The constant <paramref name="node"/> is or resolves to: a
    /// <see cref="ConstantNode"/>, or an <see cref="InstructionFieldNode"/> whose
    /// <see cref="InstructionFieldNode.ResolvedNode"/> is one.</summary>
    public static ConstantNode? Resolved(IVisitableAstNode node) => node switch {
        ConstantNode constant => constant,
        InstructionFieldNode { ResolvedNode: ConstantNode resolved } => resolved,
        _ => null
    };

    /// <summary>The raw bits of <paramref name="value"/> truncated to <paramref name="type"/>'s width.</summary>
    public static ulong Bits(DataType type, ulong value) => value & ConstantNode.Mask(type.BitWidth);

    /// <summary>Whether adding <paramref name="constant"/> subtracts an amount: a negative constant
    /// of a signed type (its sign is explicit and it sign-extends to any
    /// <paramref name="operationWidth"/>), or an unsigned value in the top 0x7F of its width (only
    /// when the constant's own width is <paramref name="operationWidth"/>: a narrower unsigned
    /// constant means a positive addition). <paramref name="magnitude"/> is that amount; the
    /// unsigned values whose negation is itself (0x80, 0x8000, ...) stay additions.</summary>
    public static bool TrySubtractionMagnitude(ConstantNode constant, BitWidth operationWidth, out ulong magnitude) {
        magnitude = 0;
        if (constant.DataType is { Signed: true }
            && constant.DataType != DataType.INT64) {
            if (!constant.IsNegative || constant.SignedValue == int.MinValue) {
                return false;
            }
            magnitude = (ulong)(-constant.SignedValue);
            return true;
        }
        if (constant.DataType.Signed || constant.DataType.BitWidth != operationWidth) {
            return false;
        }
        ulong mask = ConstantNode.Mask(constant.DataType.BitWidth);
        if (constant.Value == 0 || constant.Value < mask - 0x7F) {
            return false;
        }
        magnitude = mask + 1 - constant.Value;
        return magnitude <= 0x7F;
    }

    private static bool TryEvaluateBinary(BinaryOperationNode binary,
        Func<IVisitableAstNode, ulong?> leafValueOf, out ulong value, bool forReplacement) {
        value = 0;
        if (!TryEvaluate(binary.Left, leafValueOf, out ulong left, forReplacement)
            || !TryEvaluate(binary.Right, leafValueOf, out ulong right, forReplacement)) {
            return false;
        }
        bool signed = binary.Left.DataType.Signed || binary.Right.DataType.Signed;
        switch (binary.BinaryOperation) {
            case BinaryOperation.PLUS: value = left + right; break;
            case BinaryOperation.MINUS: value = left - right; break;
            case BinaryOperation.MULTIPLY: value = left * right; break;
            case BinaryOperation.BITWISE_AND: value = left & right; break;
            case BinaryOperation.BITWISE_OR: value = left | right; break;
            case BinaryOperation.BITWISE_XOR: value = left ^ right; break;
            case BinaryOperation.LEFT_SHIFT: value = left << (int)right; break;
            case BinaryOperation.RIGHT_SHIFT:
                value = binary.Left.DataType.Signed ? (ulong)((long)left >> (int)right) : left >> (int)right;
                break;
            case BinaryOperation.EQUAL: value = left == right ? 1UL : 0UL; return true;
            case BinaryOperation.NOT_EQUAL: value = left != right ? 1UL : 0UL; return true;
            case BinaryOperation.LESS_THAN:
                value = (signed ? (long)left < (long)right : left < right) ? 1UL : 0UL; return true;
            case BinaryOperation.GREATER_THAN:
                value = (signed ? (long)left > (long)right : left > right) ? 1UL : 0UL; return true;
            case BinaryOperation.LESS_THAN_OR_EQUAL:
                value = (signed ? (long)left <= (long)right : left <= right) ? 1UL : 0UL; return true;
            case BinaryOperation.GREATER_THAN_OR_EQUAL:
                value = (signed ? (long)left >= (long)right : left >= right) ? 1UL : 0UL; return true;
            case BinaryOperation.LOGICAL_AND:
                value = left != 0 && right != 0 ? 1UL : 0UL; return true;
            case BinaryOperation.LOGICAL_OR:
                value = left != 0 || right != 0 ? 1UL : 0UL; return true;
            default:
                return false;
        }
        value = SemanticValue(binary.DataType, value & ConstantNode.Mask(binary.DataType.BitWidth));
        return true;
    }

    private static bool TryEvaluateSelfZero(AluOperationNode alu, out ulong value) {
        value = 0;
        return alu.Call.Operation is AluOperation.Xor or AluOperation.Sub
            && alu.Call.CallNode.Arguments is [RegisterNode first, RegisterNode second]
            && first.Equals(second);
    }

    /// <summary>The value of an ALU operation whose arguments all evaluate, plus the status flags
    /// the emulator's ALU writes for them: a scratch state runs the operation twice, once with
    /// every status flag cleared and once with every status flag set (the carry flag pinned to
    /// <paramref name="carry"/>, required by the carry-reading <c>Adc</c> / <c>Sbb</c> / <c>Rcl</c> /
    /// <c>Rcr</c>). A flag equal in both runs is written; its value is the
    /// <paramref name="flags"/> entry. A flag that differs kept its input and is not listed.
    /// The supported operations are exactly those whose only status-flag input is the pinned carry
    /// or a flag's own previous value, so the two all-cleared/all-set samples decide each flag;
    /// they are not a general Boolean independence proof. Divisions and <c>UpdateFlags</c> are
    /// never evaluated.</summary>
    public static bool TryEvaluateAluFlags(AluCallValueNode call,
        Func<IVisitableAstNode, ulong?> leafValueOf, bool? carry,
        out ulong value, out List<(StatusFlags Flag, bool Set)> flags) {
        value = 0;
        flags = [];
        if (call.Operation is AluOperation.Div or AluOperation.Idiv or AluOperation.UpdateFlags
            || (call.Operation is AluOperation.Adc or AluOperation.Sbb or AluOperation.Rcl
                or AluOperation.Rcr && carry is null)) {
            return false;
        }
        IReadOnlyList<IVisitableAstNode> arguments = call.CallNode.Arguments;
        ulong[] operands = new ulong[arguments.Count];
        for (int index = 0; index < arguments.Count; index++) {
            if (!TryEvaluate(arguments[index], leafValueOf, out operands[index])) {
                return false;
            }
        }
        // One scratch state per evaluation: a shared one would let a concurrent evaluation's flag
        // writes leak into this result.
        State state = new(CpuModel.INTEL_8086);
        ulong? cleared = RunAluWithFlags(call, operands, state, false, carry,
            out StatusFlags clearedFlags);
        ulong? raised = RunAluWithFlags(call, operands, state, true, carry,
            out StatusFlags raisedFlags);
        if (cleared is not { } result || cleared != raised) {
            return false;
        }
        value = SemanticValue(call.DataType, result & ConstantNode.Mask(call.DataType.BitWidth));
        foreach ((_, _, StatusFlags flag) in IrValueSets.StatusFlagRows
                .Where(row => (clearedFlags & row.Flag) == (raisedFlags & row.Flag))) {
            flags.Add((flag, (clearedFlags & flag) != StatusFlags.None));
        }
        return true;
    }

    private static ulong? RunAluWithFlags(AluCallValueNode call, ulong[] operands, State state,
        bool set, bool? carry, out StatusFlags flags) {
        state.CarryFlag = carry ?? set;
        state.ParityFlag = set;
        state.AuxiliaryFlag = set;
        state.ZeroFlag = set;
        state.SignFlag = set;
        state.OverflowFlag = set;
        ulong? result = call.Width switch {
            BitWidth.BYTE_8 => EvaluateAlu8(call.Operation, operands, state),
            BitWidth.WORD_16 => EvaluateAlu16(call.Operation, operands, state),
            BitWidth.DWORD_32 => EvaluateAlu32(call.Operation, operands, state),
            _ => null
        };
        flags = (state.CarryFlag ? StatusFlags.Carry : StatusFlags.None)
            | (state.ParityFlag ? StatusFlags.Parity : StatusFlags.None)
            | (state.AuxiliaryFlag ? StatusFlags.Auxiliary : StatusFlags.None)
            | (state.ZeroFlag ? StatusFlags.Zero : StatusFlags.None)
            | (state.SignFlag ? StatusFlags.Sign : StatusFlags.None)
            | (state.OverflowFlag ? StatusFlags.Overflow : StatusFlags.None);
        return result;
    }

    /// <summary>The value of an ALU operation whose arguments all evaluate, computed by the
    /// emulator's own <see cref="Alu8"/> / <see cref="Alu16"/> / <see cref="Alu32"/> on a scratch
    /// state, so the constant is the emulator's result. Divisions are never evaluated (they can
    /// fault); the carry-reading <c>Adc</c> / <c>Sbb</c> / <c>Rcl</c> / <c>Rcr</c> are skipped: the
    /// leaf facts supply no carry flag value.</summary>
    private static bool TryEvaluateAlu(AluOperationNode alu,
        Func<IVisitableAstNode, ulong?> leafValueOf, out ulong value, bool forReplacement) {
        value = 0;
        AluCallValueNode call = alu.Call;
        if (call.Operation is AluOperation.Adc or AluOperation.Sbb or AluOperation.Rcl
                or AluOperation.Rcr or AluOperation.Div or AluOperation.Idiv
                or AluOperation.UpdateFlags) {
            return false;
        }
        IReadOnlyList<IVisitableAstNode> arguments = call.CallNode.Arguments;
        ulong[] operands = new ulong[arguments.Count];
        for (int index = 0; index < arguments.Count; index++) {
            if (!TryEvaluate(arguments[index], leafValueOf, out operands[index], forReplacement)) {
                return false;
            }
        }
        State state = new(CpuModel.INTEL_8086);
        ulong? result = call.Width switch {
            BitWidth.BYTE_8 => EvaluateAlu8(call.Operation, operands, state),
            BitWidth.WORD_16 => EvaluateAlu16(call.Operation, operands, state),
            BitWidth.DWORD_32 => EvaluateAlu32(call.Operation, operands, state),
            _ => null
        };
        if (result is not { } computed) {
            return false;
        }
        value = SemanticValue(alu.DataType, computed & ConstantNode.Mask(alu.DataType.BitWidth));
        return true;
    }

    private static ulong? EvaluateAlu8(AluOperation operation, ulong[] operands, State state) {
        Alu8 alu = new(state);
        return operation switch {
            AluOperation.Add when operands is [ulong a, ulong b] => alu.Add((byte)a, (byte)b),
            AluOperation.Adc when operands is [ulong a, ulong b] => alu.Add((byte)a, (byte)b, true),
            AluOperation.Sub when operands is [ulong a, ulong b] => alu.Sub((byte)a, (byte)b),
            AluOperation.Sbb when operands is [ulong a, ulong b] => alu.Sub((byte)a, (byte)b, true),
            AluOperation.And when operands is [ulong a, ulong b] => alu.And((byte)a, (byte)b),
            AluOperation.Or when operands is [ulong a, ulong b] => alu.Or((byte)a, (byte)b),
            AluOperation.Xor when operands is [ulong a, ulong b] => alu.Xor((byte)a, (byte)b),
            AluOperation.Inc when operands is [ulong a] => alu.Inc((byte)a),
            AluOperation.Dec when operands is [ulong a] => alu.Dec((byte)a),
            AluOperation.Shl when operands is [ulong a, ulong count] => alu.Shl((byte)a, (int)count),
            AluOperation.Shr when operands is [ulong a, ulong count] => alu.Shr((byte)a, (int)count),
            AluOperation.Sar when operands is [ulong a, ulong count] => alu.Sar((byte)a, (int)count),
            AluOperation.Rcl when operands is [ulong a, ulong count] => alu.Rcl((byte)a, (byte)count),
            AluOperation.Rcr when operands is [ulong a, ulong count] => alu.Rcr((byte)a, (int)count),
            AluOperation.Rol when operands is [ulong a, ulong count] => alu.Rol((byte)a, (byte)count),
            AluOperation.Ror when operands is [ulong a, ulong count] => alu.Ror((byte)a, (int)count),
            AluOperation.Shld when operands is [ulong a, ulong b, ulong count] => alu.Shld((byte)a, (byte)b, (byte)count),
            AluOperation.Shrd when operands is [ulong a, ulong b, ulong count] => alu.Shrd((byte)a, (byte)b, (byte)count),
            AluOperation.Mul when operands is [ulong a, ulong b] => alu.Mul((byte)a, (byte)b),
            AluOperation.Imul when operands is [ulong a, ulong b] => (ulong)alu.Imul((sbyte)a, (sbyte)b),
            _ => null
        };
    }

    private static ulong? EvaluateAlu16(AluOperation operation, ulong[] operands, State state) {
        Alu16 alu = new(state);
        return operation switch {
            AluOperation.Add when operands is [ulong a, ulong b] => alu.Add((ushort)a, (ushort)b),
            AluOperation.Adc when operands is [ulong a, ulong b] => alu.Add((ushort)a, (ushort)b, true),
            AluOperation.Sub when operands is [ulong a, ulong b] => alu.Sub((ushort)a, (ushort)b),
            AluOperation.Sbb when operands is [ulong a, ulong b] => alu.Sub((ushort)a, (ushort)b, true),
            AluOperation.And when operands is [ulong a, ulong b] => alu.And((ushort)a, (ushort)b),
            AluOperation.Or when operands is [ulong a, ulong b] => alu.Or((ushort)a, (ushort)b),
            AluOperation.Xor when operands is [ulong a, ulong b] => alu.Xor((ushort)a, (ushort)b),
            AluOperation.Inc when operands is [ulong a] => alu.Inc((ushort)a),
            AluOperation.Dec when operands is [ulong a] => alu.Dec((ushort)a),
            AluOperation.Shl when operands is [ulong a, ulong count] => alu.Shl((ushort)a, (int)count),
            AluOperation.Shr when operands is [ulong a, ulong count] => alu.Shr((ushort)a, (int)count),
            AluOperation.Sar when operands is [ulong a, ulong count] => alu.Sar((ushort)a, (int)count),
            AluOperation.Rcl when operands is [ulong a, ulong count] => alu.Rcl((ushort)a, (byte)count),
            AluOperation.Rcr when operands is [ulong a, ulong count] => alu.Rcr((ushort)a, (int)count),
            AluOperation.Rol when operands is [ulong a, ulong count] => alu.Rol((ushort)a, (byte)count),
            AluOperation.Ror when operands is [ulong a, ulong count] => alu.Ror((ushort)a, (int)count),
            AluOperation.Shld when operands is [ulong a, ulong b, ulong count] => alu.Shld((ushort)a, (ushort)b, (byte)count),
            AluOperation.Shrd when operands is [ulong a, ulong b, ulong count] => alu.Shrd((ushort)a, (ushort)b, (byte)count),
            AluOperation.Mul when operands is [ulong a, ulong b] => alu.Mul((ushort)a, (ushort)b),
            AluOperation.Imul when operands is [ulong a, ulong b] => (ulong)alu.Imul((short)a, (short)b),
            _ => null
        };
    }

    private static ulong? EvaluateAlu32(AluOperation operation, ulong[] operands, State state) {
        Alu32 alu = new(state);
        return operation switch {
            AluOperation.Add when operands is [ulong a, ulong b] => alu.Add((uint)a, (uint)b),
            AluOperation.Adc when operands is [ulong a, ulong b] => alu.Add((uint)a, (uint)b, true),
            AluOperation.Sub when operands is [ulong a, ulong b] => alu.Sub((uint)a, (uint)b),
            AluOperation.Sbb when operands is [ulong a, ulong b] => alu.Sub((uint)a, (uint)b, true),
            AluOperation.And when operands is [ulong a, ulong b] => alu.And((uint)a, (uint)b),
            AluOperation.Or when operands is [ulong a, ulong b] => alu.Or((uint)a, (uint)b),
            AluOperation.Xor when operands is [ulong a, ulong b] => alu.Xor((uint)a, (uint)b),
            AluOperation.Inc when operands is [ulong a] => alu.Inc((uint)a),
            AluOperation.Dec when operands is [ulong a] => alu.Dec((uint)a),
            AluOperation.Shl when operands is [ulong a, ulong count] => alu.Shl((uint)a, (int)count),
            AluOperation.Shr when operands is [ulong a, ulong count] => alu.Shr((uint)a, (int)count),
            AluOperation.Sar when operands is [ulong a, ulong count] => alu.Sar((uint)a, (int)count),
            AluOperation.Rcl when operands is [ulong a, ulong count] => alu.Rcl((uint)a, (byte)count),
            AluOperation.Rcr when operands is [ulong a, ulong count] => alu.Rcr((uint)a, (int)count),
            AluOperation.Rol when operands is [ulong a, ulong count] => alu.Rol((uint)a, (byte)count),
            AluOperation.Ror when operands is [ulong a, ulong count] => alu.Ror((uint)a, (int)count),
            AluOperation.Shld when operands is [ulong a, ulong b, ulong count] => alu.Shld((uint)a, (uint)b, (byte)count),
            AluOperation.Shrd when operands is [ulong a, ulong b, ulong count] => alu.Shrd((uint)a, (uint)b, (byte)count),
            AluOperation.Mul when operands is [ulong a, ulong b] => alu.Mul((uint)a, (uint)b),
            AluOperation.Imul when operands is [ulong a, ulong b] => (ulong)alu.Imul((int)a, (int)b),
            _ => null
        };
    }

    /// <summary>The C# value of <paramref name="bits"/> read as <paramref name="type"/>: sign-extended
    /// for a signed type, the raw bits otherwise.</summary>
    public static ulong SemanticValue(DataType type, ulong bits) =>
        type.Signed ? (ulong)new ConstantNode(type, bits).SignedValue : bits;
}
