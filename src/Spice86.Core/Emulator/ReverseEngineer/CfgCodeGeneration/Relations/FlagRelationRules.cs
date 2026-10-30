namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Relations;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>
/// The relation a flag condition of an ALU setter means: each rule is the expression over the setter
/// operands or result that is equal to a <c>BuildSetCondition</c> code evaluated right after the setter.
/// </summary>
internal static class FlagRelationRules {
    private static readonly AstBuilder Builder = new();

    /// <summary>
    /// The condition code of <paramref name="condition"/>: the code of a <c>BuildSetCondition</c> shape, and
    /// of a single flag read (Carry 2, Zero 4, Sign 8, Overflow 0, Parity 10). <c>null</c> otherwise.
    /// </summary>
    public static int? ConditionCodeOf(ValueNode condition) {
        if (condition is CpuFlagNode flag) {
            int? code = flag.FlagMask switch {
                Flags.Carry => 2,
                Flags.Zero => 4,
                Flags.Sign => 8,
                Flags.Overflow => 0,
                Flags.Parity => 10,
                _ => null
            };
            if (code is not null) {
                return code;
            }
        }
        for (int code = 0; code < 16; code++) {
            if (Builder.Flag.BuildSetCondition(code).Equals(condition)) {
                return code;
            }
        }
        return null;
    }

    /// <summary>
    /// The relation equal to condition <paramref name="conditionCode"/> evaluated right after
    /// <paramref name="setter"/>, built from the available members of <paramref name="operands"/>, or
    /// <c>null</c> when no rule applies or a rule needs an operand that is null. A result form wins over an
    /// operand form when both apply.
    /// </summary>
    public static ValueNode? TryRelation(FlagSetter setter, int conditionCode, FlagRelationOperands operands) =>
        setter.Operation switch {
            AluOperation.Sub => Sub(setter, conditionCode, operands),
            AluOperation.And => AndOrXor(conditionCode, operands, true),
            AluOperation.Or or AluOperation.Xor => AndOrXor(conditionCode, operands, false),
            AluOperation.Add => Add(conditionCode, operands),
            AluOperation.Inc or AluOperation.Dec => IncDec(setter, conditionCode, operands),
            AluOperation.Adc or AluOperation.Sbb => ResultOnly(conditionCode, operands),
            AluOperation.Shl => Shl(setter, conditionCode, operands),
            AluOperation.Shr => ShrOrSar(setter, conditionCode, operands, false),
            AluOperation.Sar => ShrOrSar(setter, conditionCode, operands, true),
            AluOperation.Rol or AluOperation.Ror => RolOrRor(setter, conditionCode, operands),
            _ => null
        };

    private static int Width(FlagSetter setter) => (int)setter.Width;

    private static ulong Msb(FlagSetter setter) => 1UL << (Width(setter) - 1);

    private static ConstantNode Constant(FlagSetter setter, ulong value) =>
        new(DataType.UnsignedFromBitWidth(setter.Width), value);

    private static ConstantNode BoolConstant(bool value) => new(DataType.BOOL, (ulong)(value ? 1 : 0));

    private static BinaryOperationNode Relation(ValueNode left, BinaryOperation operation, ValueNode right) =>
        new(DataType.BOOL, left, operation, right);

    private static TypeConversionNode Signed(ValueNode value) =>
        new(DataType.SignedFromBitWidth(value.DataType.BitWidth), value);

    private static ConstantNode ZeroOf(ValueNode value) => new(value.DataType, 0);

    private static ConstantNode SignedZeroOf(ValueNode value) =>
        new(DataType.SignedFromBitWidth(value.DataType.BitWidth), 0);

    private static bool IsZeroConstant(ValueNode? value) => value is ConstantNode { Value: 0 };

    private static ValueNode? ZeroSignResult(int code, ValueNode result) => code switch {
        4 => Relation(result, BinaryOperation.EQUAL, ZeroOf(result)),
        5 => Relation(result, BinaryOperation.NOT_EQUAL, ZeroOf(result)),
        8 => Relation(Signed(result), BinaryOperation.LESS_THAN, SignedZeroOf(result)),
        9 => Relation(Signed(result), BinaryOperation.GREATER_THAN_OR_EQUAL, SignedZeroOf(result)),
        _ => null
    };

    private static ValueNode? AndLike(int code, ValueNode value) => code switch {
        4 or 6 => Relation(value, BinaryOperation.EQUAL, ZeroOf(value)),
        5 or 7 => Relation(value, BinaryOperation.NOT_EQUAL, ZeroOf(value)),
        8 or 12 => Relation(Signed(value), BinaryOperation.LESS_THAN, SignedZeroOf(value)),
        9 or 13 => Relation(Signed(value), BinaryOperation.GREATER_THAN_OR_EQUAL, SignedZeroOf(value)),
        14 => Relation(Signed(value), BinaryOperation.LESS_THAN_OR_EQUAL, SignedZeroOf(value)),
        15 => Relation(Signed(value), BinaryOperation.GREATER_THAN, SignedZeroOf(value)),
        _ => null
    };

    private static ValueNode Masked(ValueNode left, ValueNode right) {
        if (left is RegisterNode && left.Equals(right)) {
            return left;
        }
        return new BinaryOperationNode(left.DataType, left, BinaryOperation.BITWISE_AND, right);
    }

    private static ValueNode? Sub(FlagSetter setter, int code, FlagRelationOperands operands) {
        bool neg = IsZeroConstant(operands.Left);
        switch (code) {
            case 2:
                if (neg && operands.Result is { } negResultBelow) {
                    return Relation(negResultBelow, BinaryOperation.NOT_EQUAL, ZeroOf(negResultBelow));
                }
                if (operands is { Left: null, Result: { } subResBelow, Right: { } subRightBelow }) {
                    return Relation(TruncatedSum(setter, subResBelow, subRightBelow),
                        BinaryOperation.LESS_THAN, subRightBelow);
                }
                return CompareOperands(operands, BinaryOperation.LESS_THAN);
            case 3:
                if (neg && operands.Result is { } negResultAbove) {
                    return Relation(negResultAbove, BinaryOperation.EQUAL, ZeroOf(negResultAbove));
                }
                if (operands is { Left: null, Result: { } subResAbove, Right: { } subRightAbove }) {
                    return Relation(TruncatedSum(setter, subResAbove, subRightAbove),
                        BinaryOperation.GREATER_THAN_OR_EQUAL, subRightAbove);
                }
                return CompareOperands(operands, BinaryOperation.GREATER_THAN_OR_EQUAL);
            case 4 or 5: {
                BinaryOperation equality = code == 4 ? BinaryOperation.EQUAL : BinaryOperation.NOT_EQUAL;
                if (operands.Result is { } result) {
                    return Relation(result, equality, ZeroOf(result));
                }
                return CompareOperands(operands, equality);
            }
            case 6:
                return CompareOperands(operands, BinaryOperation.LESS_THAN_OR_EQUAL);
            case 7:
                return CompareOperands(operands, BinaryOperation.GREATER_THAN);
            case 8 or 9: {
                BinaryOperation sign = code == 8 ? BinaryOperation.LESS_THAN : BinaryOperation.GREATER_THAN_OR_EQUAL;
                if (operands.Result is { } result) {
                    return Relation(Signed(result), sign, SignedZeroOf(result));
                }
                if (operands is { Left: { } left, Right: { } right }) {
                    ValueNode difference = new BinaryOperationNode(left.DataType, left, BinaryOperation.MINUS, right);
                    return Relation(Signed(difference), sign, SignedZeroOf(difference));
                }
                return null;
            }
            case 12 or 13 or 14 or 15: {
                if (operands is not { Left: { } left, Right: { } right }) {
                    return null;
                }
                BinaryOperation signedCompare = code switch {
                    12 => BinaryOperation.LESS_THAN,
                    13 => BinaryOperation.GREATER_THAN_OR_EQUAL,
                    14 => BinaryOperation.LESS_THAN_OR_EQUAL,
                    _ => BinaryOperation.GREATER_THAN
                };
                return Relation(Signed(left), signedCompare, Signed(right));
            }
            default:
                return null;
        }
    }

    private static ValueNode? CompareOperands(FlagRelationOperands operands, BinaryOperation operation) =>
        operands is { Left: { } left, Right: { } right } ? Relation(left, operation, right) : null;

    private static ValueNode? AndOrXor(int code, FlagRelationOperands operands, bool andSemantics) {
        if (code is 0 or 2) {
            return BoolConstant(false);
        }
        if (code is 1 or 3) {
            return BoolConstant(true);
        }
        if (operands.Result is { } result) {
            return AndLike(code, result);
        }
        if (andSemantics && operands is { Left: { } left, Right: { } right }) {
            return AndLike(code, Masked(left, right));
        }
        return null;
    }

    /// <summary><c>Res + R mod 2^w</c> recomputes the subtracted left operand of a sub whose destination
    /// overwrote it, so the borrow <c>L &lt; R</c> becomes <c>(T)(Res + R) &lt; R</c>.</summary>
    private static ValueNode TruncatedSum(FlagSetter setter, ValueNode result, ValueNode right) {
        DataType unsigned = DataType.UnsignedFromBitWidth(setter.Width);
        return new TypeConversionNode(unsigned,
            new BinaryOperationNode(unsigned, result, BinaryOperation.PLUS, right));
    }

    private static ValueNode? Add(int code, FlagRelationOperands operands) => code switch {
        2 => SumCarryRelation(operands, BinaryOperation.LESS_THAN),
        3 => SumCarryRelation(operands, BinaryOperation.GREATER_THAN_OR_EQUAL),
        _ => operands.Result is { } result ? ZeroSignResult(code, result) : null
    };

    /// <summary>An add carry is <c>Res &lt; L</c> and equally <c>Res &lt; R</c>; the right operand form
    /// covers a setter whose destination is also its left operand (<c>add ax,cx</c> overwrites L).</summary>
    private static ValueNode? SumCarryRelation(FlagRelationOperands operands, BinaryOperation operation) {
        if (operands.Result is not { } result) {
            return null;
        }
        if (operands.Left is { } left) {
            return Relation(result, operation, left);
        }
        return operands.Right is { } right ? Relation(result, operation, right) : null;
    }

    private static ValueNode? IncDec(FlagSetter setter, int code, FlagRelationOperands operands) {
        if (operands.Result is not { } result) {
            return null;
        }
        ulong bound = setter.Operation == AluOperation.Inc ? Msb(setter) : Msb(setter) - 1;
        return code switch {
            0 => Relation(result, BinaryOperation.EQUAL, Constant(setter, bound)),
            1 => Relation(result, BinaryOperation.NOT_EQUAL, Constant(setter, bound)),
            _ => ZeroSignResult(code, result)
        };
    }

    private static ValueNode? ResultOnly(int code, FlagRelationOperands operands) =>
        operands.Result is { } result ? ZeroSignResult(code, result) : null;

    private static bool ValidCount(FlagSetter setter) =>
        setter.Count is { } count && count >= 1 && count <= Width(setter) - 1;

    private static ValueNode BitTest(ValueNode value, FlagSetter setter, ulong mask, bool set) {
        ValueNode masked = new BinaryOperationNode(value.DataType, value, BinaryOperation.BITWISE_AND,
            Constant(setter, mask));
        return Relation(masked, set ? BinaryOperation.NOT_EQUAL : BinaryOperation.EQUAL, ZeroOf(masked));
    }

    private static ValueNode? Shl(FlagSetter setter, int code, FlagRelationOperands operands) {
        if (!ValidCount(setter) || setter.Count is not { } count) {
            return null;
        }
        switch (code) {
            case 2 or 3:
                return operands.Left is { } left
                    ? BitTest(left, setter, 1UL << (Width(setter) - count), code == 2)
                    : null;
            case 0 or 1 when count == 1:
                if (operands.Left is not { } shlLeft) {
                    return null;
                }
                ValueNode shifted = new BinaryOperationNode(shlLeft.DataType, shlLeft, BinaryOperation.LEFT_SHIFT,
                    new ConstantNode(DataType.INT32, 1));
                ValueNode difference = new BinaryOperationNode(shlLeft.DataType, shlLeft,
                    BinaryOperation.BITWISE_XOR, shifted);
                return BitTest(difference, setter, Msb(setter), code == 0);
            default:
                return operands.Result is { } result ? ZeroSignResult(code, result) : null;
        }
    }

    private static ValueNode? ShrOrSar(FlagSetter setter, int code, FlagRelationOperands operands, bool arithmetic) {
        if (!ValidCount(setter) || setter.Count is not { } count) {
            return null;
        }
        switch (code) {
            case 2 or 3:
                return operands.Left is { } left
                    ? BitTest(left, setter, 1UL << (count - 1), code == 2)
                    : null;
            case 0 or 1 when count == 1 && arithmetic:
                return BoolConstant(code == 1);
            case 0 or 1 when count == 1:
                return operands.Left is { } msbLeft ? BitTest(msbLeft, setter, Msb(setter), code == 0) : null;
            default:
                return operands.Result is { } result ? ZeroSignResult(code, result) : null;
        }
    }

    private static ValueNode? RolOrRor(FlagSetter setter, int code, FlagRelationOperands operands) {
        if (!ValidCount(setter)) {
            return null;
        }
        if (code is not (2 or 3) || operands.Result is not { } result) {
            return null;
        }
        ulong mask = setter.Operation == AluOperation.Rol ? 1UL : Msb(setter);
        return BitTest(result, setter, mask, code == 2);
    }
}
