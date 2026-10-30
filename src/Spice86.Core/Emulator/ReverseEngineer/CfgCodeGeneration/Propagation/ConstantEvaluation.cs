namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

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
        out ulong value) {
        switch (node) {
            case ConstantNode constant:
                value = SemanticValue(constant.DataType, constant.Value);
                return true;
            case InstructionFieldNode field:
                return TryEvaluate(field.ResolvedNode, leafValueOf, out value);
            case RegisterNode or VariableReferenceNode or CpuFlagNode:
                ulong? leafValue = leafValueOf(node);
                value = leafValue ?? 0;
                return leafValue.HasValue;
            case ConditionalValueNode conditional:
                if (!TryEvaluate(conditional.Condition, leafValueOf, out ulong condition)) {
                    value = 0;
                    return false;
                }
                return TryEvaluate(condition != 0 ? conditional.WhenTrue : conditional.WhenFalse,
                    leafValueOf, out value);
            case TypeConversionNode conversion:
                if (!TryEvaluate(conversion.Value, leafValueOf, out ulong converted)) {
                    value = 0;
                    return false;
                }
                value = SemanticValue(conversion.DataType,
                    converted & ConstantNode.Mask(conversion.DataType.BitWidth));
                return true;
            case UnaryOperationNode unary:
                if (!TryEvaluate(unary.Value, leafValueOf, out ulong operand)) {
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
                return TryEvaluateBinary(binary, leafValueOf, out value);
            case AluOperationNode alu:
                return TryEvaluateSelfZero(alu, out value);
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
        Func<IVisitableAstNode, ulong?> leafValueOf, out ulong value) {
        value = 0;
        if (!TryEvaluate(binary.Left, leafValueOf, out ulong left)
            || !TryEvaluate(binary.Right, leafValueOf, out ulong right)) {
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
        return alu.Operation is AluOperation.Xor or AluOperation.Sub
            && alu.Call.CallNode.Arguments is [RegisterNode first, RegisterNode second]
            && first.Equals(second);
    }

    /// <summary>The C# value of <paramref name="bits"/> read as <paramref name="type"/>: sign-extended
    /// for a signed type, the raw bits otherwise.</summary>
    public static ulong SemanticValue(DataType type, ulong bits) =>
        type.Signed ? (ulong)new ConstantNode(type, bits).SignedValue : bits;
}
