namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

/// <summary>Pure algebra and mask simplifications of <see cref="IrConstantFolder"/>: identities
/// whose constant forces the result, same-operation constant masks, masks a width or a shift makes
/// redundant, inverse pairs, comparison and boolean rewrites, and arithmetic pushed into the
/// constant arms of a <see cref="ConditionalValueNode"/>. Every rule keeps the node's width
/// semantics: the printed C# promotes byte and word operands, so a rewrite either requires the
/// operand's own type to match or wraps it in an explicit conversion through
/// <see cref="WithType"/>. A rule may drop an operand only when it can be evaluated away
/// (<see cref="CanDiscard"/>, a pure expression under <see cref="IrVerifier.IsPureArm"/>); a rule
/// that keeps it exactly once applies to impure operands too. Bitwise constants compare after
/// truncation to the result width (<see cref="ConstantEvaluation.Bits"/>); comparisons compare the
/// untruncated semantic value (<see cref="ConstantEvaluation.SemanticValue"/>).</summary>
internal static partial class IrConstantFolder {
    /// <summary>Simplifies <paramref name="node"/>, whose children are already folded. Returns the
    /// node unchanged when no rule applies. Never called on an ASSIGN left side.</summary>
    private static ValueNode FoldAlgebra(ValueNode node) => node switch {
        BinaryOperationNode binary => FoldBinaryAlgebra(binary),
        UnaryOperationNode unary => FoldUnaryAlgebra(unary),
        _ => node
    };

    private static ValueNode WithType(DataType type, ValueNode value) =>
        type == value.DataType ? value : new TypeConversionNode(type, value);

    private static bool CanDiscard(ValueNode node) => IrVerifier.IsPureArm(node);

    private static bool IsInteger(DataType type) => type.BitWidth
        is BitWidth.BYTE_8 or BitWidth.WORD_16 or BitWidth.DWORD_32 or BitWidth.QWORD_64;

    private static ulong MaskOf(DataType type) => ConstantNode.Mask(type.BitWidth);

    /// <summary><paramref name="node"/>'s raw bits truncated to <paramref name="type"/>'s width
    /// when it resolves to a constant, else null.</summary>
    private static ulong? BitsOf(ValueNode node, DataType type) =>
        ConstantEvaluation.Resolved(node) is { } constant
            ? ConstantEvaluation.Bits(type, constant.Value)
            : null;

    /// <summary><paramref name="node"/>'s untruncated semantic value when it resolves to a
    /// constant, else null.</summary>
    private static ulong? SemanticOf(ValueNode node) =>
        ConstantEvaluation.Resolved(node) is { } constant
            ? ConstantEvaluation.SemanticValue(constant.DataType, constant.Value)
            : null;

    private static ConstantNode Const(DataType type, ulong bits) =>
        new(type, ConstantEvaluation.Bits(type, bits));

    private static ValueNode FoldBinaryAlgebra(BinaryOperationNode node) {
        if (node.BinaryOperation is BinaryOperation.ASSIGN) {
            return node;
        }
        return FoldDivideModulo(node)
            ?? PushIntoConditionalArms(node)
            ?? FoldIdentities(node)
            ?? FoldConstantMasks(node)
            ?? FoldKnownWidth(node)
            ?? FoldShiftMasks(node)
            ?? FoldEquality(node)
            ?? FoldBoolean(node)
            ?? FoldUnsignedCompare(node)
            ?? FoldRange(node)
            ?? node;
    }

    /// <summary><c>(x - lower) &gt; span</c> and <c>&lt;= span</c> on an unsigned operand the
    /// subtraction wraps at: the truncated result is out of range <c>[lower, lower+span]</c> or
    /// inside it, so the comparison reads <c>x &lt; lower || x &gt; upper</c> or
    /// <c>x &gt;= lower &amp;&amp; x &lt;= upper</c> on the pure source at the operand width. A
    /// wider untruncated subtraction never wrapped, so only a same-width (or truncating) operand
    /// matches.</summary>
    private static ValueNode? FoldRange(BinaryOperationNode node) {
        if (node.BinaryOperation is not (BinaryOperation.GREATER_THAN
                or BinaryOperation.LESS_THAN_OR_EQUAL)
            || node.Left.DataType.Signed || !IsInteger(node.Left.DataType)) {
            return null;
        }
        BitWidth width = node.Left.DataType.BitWidth;
        ulong mask = MaskOf(node.Left.DataType);
        if (SemanticOf(node.Right) is not { } span || span > mask) {
            return null;
        }
        ValueNode inner = node.Left is TypeConversionNode { Value: { } conversion }
            ? conversion : node.Left;
        if (inner is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.MINUS, Left: { } x, Right: { } lowerNode }
            || inner.DataType.BitWidth < width
            || SemanticOf(lowerNode) is not { } lower || lower > mask
            || span > mask - lower
            || !CanDiscard(x)) {
            return null;
        }
        // The source must be pure and unsigned at the operand width; a wider source truncates
        // there first.
        ValueNode source = x.DataType.BitWidth == width ? x
            : new TypeConversionNode(DataType.UnsignedFromBitWidth(width), x);
        if (source.DataType.Signed) {
            return null;
        }
        ConstantNode lowerConstant = new(node.Left.DataType, lower);
        ConstantNode upperConstant = new(node.Left.DataType, lower + span);
        if (node.BinaryOperation == BinaryOperation.GREATER_THAN) {
            return new BinaryOperationNode(DataType.BOOL,
                new BinaryOperationNode(DataType.BOOL, source, BinaryOperation.LESS_THAN,
                    lowerConstant),
                BinaryOperation.LOGICAL_OR,
                new BinaryOperationNode(DataType.BOOL, source, BinaryOperation.GREATER_THAN,
                    upperConstant));
        }
        return new BinaryOperationNode(DataType.BOOL,
            new BinaryOperationNode(DataType.BOOL, source,
                BinaryOperation.GREATER_THAN_OR_EQUAL, lowerConstant),
            BinaryOperation.LOGICAL_AND,
            new BinaryOperationNode(DataType.BOOL, source,
                BinaryOperation.LESS_THAN_OR_EQUAL, upperConstant));
    }

    /// <summary><c>(x / d1) / d2</c> is <c>x / (d1 * d2)</c> for positive constant divisors whose
    /// product fits the node's type, and <c>x - (x / d) * d</c> is <c>x % d</c> when the same
    /// operand and divisor repeat: safe for unsigned types and for positive signed divisors.</summary>
    private static ValueNode? FoldDivideModulo(BinaryOperationNode node) {
        if (!IsInteger(node.DataType)) {
            return null;
        }
        // Positive semantic values only: a negative signed constant reads as a huge ulong, and a
        // value beyond the type's maximum can never be a divisor constant of that type. The
        // product guard runs before the multiplication so it cannot overflow ulong.
        ulong max = node.DataType.Signed ? MaskOf(node.DataType) / 2 : MaskOf(node.DataType);
        if (node.BinaryOperation == BinaryOperation.DIVIDE
            && node.Left is BinaryOperationNode { BinaryOperation: BinaryOperation.DIVIDE,
                DataType: { } innerType } inner
            && innerType == node.DataType && inner.Left.DataType == node.DataType
            && SemanticOf(inner.Right) is { } first && first > 0 && first <= max
            && SemanticOf(node.Right) is { } second && second > 0 && second <= max
            && first <= max / second) {
            return new BinaryOperationNode(node.DataType, inner.Left, BinaryOperation.DIVIDE,
                Const(node.DataType, first * second));
        }
        if (node.BinaryOperation == BinaryOperation.MINUS
            && node.Right is BinaryOperationNode { BinaryOperation: BinaryOperation.MULTIPLY,
                DataType: { } mulType } mul
            && mulType == node.DataType && node.Left.DataType == node.DataType
            && CanDiscard(node.Left)) {
            foreach ((ValueNode maybeDivide, ValueNode maybeDivisor) in
                new[] { (mul.Left, mul.Right), (mul.Right, mul.Left) }) {
                if (maybeDivide is BinaryOperationNode { BinaryOperation: BinaryOperation.DIVIDE,
                        DataType: { } divideType } divide
                    && divideType == node.DataType && divide.Left.DataType == node.DataType
                    && divide.Left.Equals(node.Left) && maybeDivisor.Equals(divide.Right)
                    && SemanticOf(divide.Right) is { } divisor && divisor > 0 && divisor <= max) {
                    return new BinaryOperationNode(node.DataType, node.Left,
                        BinaryOperation.MODULO, divide.Right);
                }
            }
        }
        return null;
    }

    private static ValueNode FoldUnaryAlgebra(UnaryOperationNode node) {
        // ~~x, -(-x) and !!x are x only when the whole chain works at one width: a different inner
        // or operand width changes the result and keeps the explicit conversions.
        if (node.Value is UnaryOperationNode { UnaryOperation: { } inner } inverse
            && inner == node.UnaryOperation
            && inverse.DataType == node.DataType
            && inverse.Value.DataType == node.DataType) {
            return inverse.Value;
        }
        return PushUnaryIntoConditionalArms(node) ?? (ValueNode)node;
    }

    /// <summary><paramref name="node"/>'s condition and constant arms when it is a
    /// <see cref="ConditionalValueNode"/> with both arms constant or a conversion of a BOOL value,
    /// whose arms are 1 and 0.</summary>
    private static (ValueNode Condition, ValueNode WhenTrue, ValueNode WhenFalse)? ConstantArms(
        ValueNode node) => node switch {
            ConditionalValueNode conditional
                when ConstantEvaluation.Resolved(conditional.WhenTrue) is not null
                    && ConstantEvaluation.Resolved(conditional.WhenFalse) is not null
                => (conditional.Condition, conditional.WhenTrue, conditional.WhenFalse),
            TypeConversionNode { Value.DataType: { } sourceType } conversion
                when sourceType == DataType.BOOL
                => (conversion.Value, new ConstantNode(conversion.DataType, 1),
                    new ConstantNode(conversion.DataType, 0)),
            _ => null
        };

    /// <summary><paramref name="node"/> evaluates to when <paramref name="operand"/> takes the
    /// semantic value <paramref name="arm"/>: the operator applied at the node's own width.</summary>
    private static ulong EvalArm(BinaryOperationNode node, ValueNode operand, ValueNode arm) {
        ValueNode left = ReferenceEquals(operand, node.Left) ? arm : node.Left;
        ValueNode right = ReferenceEquals(operand, node.Right) ? arm : node.Right;
        BinaryOperationNode applied = new(node.DataType, left, node.BinaryOperation, right);
        ConstantEvaluation.TryEvaluateReplacing(applied, _ => null, out ulong value);
        return ConstantEvaluation.Bits(node.DataType, value);
    }

    private static ulong EvalArm(UnaryOperationNode node, ValueNode arm) {
        UnaryOperationNode applied = new(node.DataType, node.UnaryOperation, arm);
        ConstantEvaluation.TryEvaluateReplacing(applied, _ => null, out ulong value);
        return ConstantEvaluation.Bits(node.DataType, value);
    }

    /// <summary><c>+</c> and <c>-</c> with one constant and one conditional-or-bool operand move
    /// inside the arms: <c>0 - (ushort)b</c> prints <c>b ? 0xFFFF : 0</c>, keeping the condition
    /// evaluated once.</summary>
    private static ValueNode? PushIntoConditionalArms(BinaryOperationNode node) {
        if (node.BinaryOperation is not (BinaryOperation.PLUS or BinaryOperation.MINUS)
            || !IsInteger(node.DataType)) {
            return null;
        }
        foreach ((ValueNode arms, ValueNode other) in
            new[] { (node.Left, node.Right), (node.Right, node.Left) }) {
            if (ConstantArms(arms) is not { } pair
                || ConstantEvaluation.Resolved(other) is null) {
                continue;
            }
            ulong whenTrue = EvalArm(node, arms, pair.WhenTrue);
            ulong whenFalse = EvalArm(node, arms, pair.WhenFalse);
            return (ValueNode)FoldConditional(new ConditionalValueNode(node.DataType, pair.Condition,
                Const(node.DataType, whenTrue), Const(node.DataType, whenFalse)),
                NoVariableConstants);
        }
        return null;
    }

    /// <summary>Unary negate and bitwise-not of a conditional-or-bool operand move inside the
    /// arms.</summary>
    private static ValueNode? PushUnaryIntoConditionalArms(UnaryOperationNode node) {
        if (node.UnaryOperation is not (UnaryOperation.NEGATE or UnaryOperation.BITWISE_NOT)
            || !IsInteger(node.DataType)
            || ConstantArms(node.Value) is not { } pair) {
            return null;
        }
        return (ValueNode)FoldConditional(new ConditionalValueNode(node.DataType, pair.Condition,
            Const(node.DataType, EvalArm(node, pair.WhenTrue)),
            Const(node.DataType, EvalArm(node, pair.WhenFalse))), NoVariableConstants);
    }

    /// <summary><c>x*1</c>, <c>x &amp; allOnes</c>, <c>x ^ allOnes</c> and the constant-forced
    /// <c>x&amp;0</c>, <c>x*0</c>, <c>x | allOnes</c>, plus <c>x op x</c> and the complement
    /// <c>x &amp; ~x</c> / <c>x | ~x</c> / <c>x ^ ~x</c>.</summary>
    private static ValueNode? FoldIdentities(BinaryOperationNode node) {
        DataType type = node.DataType;
        if (!IsInteger(type)) {
            return null;
        }
        ulong mask = MaskOf(type);
        ulong? leftBits = BitsOf(node.Left, type);
        ulong? rightBits = BitsOf(node.Right, type);
        // A zero term leaves the other operand when it already carries the node's type;
        // <see cref="FoldIdentity"/> applies the same rule on the Fold path, and the
        // mask-combination recursion needs it here.
        if (rightBits == 0 && node.Left.DataType == type
            && node.BinaryOperation is BinaryOperation.PLUS or BinaryOperation.MINUS
            or BinaryOperation.BITWISE_OR or BinaryOperation.BITWISE_XOR
            or BinaryOperation.LEFT_SHIFT or BinaryOperation.RIGHT_SHIFT) {
            return node.Left;
        }
        if (leftBits == 0 && node.Right.DataType == type
            && node.BinaryOperation is BinaryOperation.PLUS or BinaryOperation.BITWISE_OR
            or BinaryOperation.BITWISE_XOR) {
            return node.Right;
        }
        switch (node.BinaryOperation) {
            case BinaryOperation.MULTIPLY:
                if (rightBits == 1) {
                    return WithType(type, node.Left);
                }
                if (leftBits == 1) {
                    return WithType(type, node.Right);
                }
                if (rightBits == 0 && CanDiscard(node.Left)) {
                    return Const(type, 0);
                }
                if (leftBits == 0 && CanDiscard(node.Right)) {
                    return Const(type, 0);
                }
                break;
            case BinaryOperation.BITWISE_AND:
                if (rightBits == mask) {
                    return WithType(type, node.Left);
                }
                if (leftBits == mask) {
                    return WithType(type, node.Right);
                }
                if (rightBits == 0 && CanDiscard(node.Left)) {
                    return Const(type, 0);
                }
                if (leftBits == 0 && CanDiscard(node.Right)) {
                    return Const(type, 0);
                }
                break;
            case BinaryOperation.BITWISE_OR:
                if (rightBits == mask && CanDiscard(node.Left)) {
                    return Const(type, mask);
                }
                if (leftBits == mask && CanDiscard(node.Right)) {
                    return Const(type, mask);
                }
                break;
            case BinaryOperation.BITWISE_XOR:
                if (rightBits == mask) {
                    return new UnaryOperationNode(type, UnaryOperation.BITWISE_NOT,
                        WithType(type, node.Left));
                }
                if (leftBits == mask) {
                    return new UnaryOperationNode(type, UnaryOperation.BITWISE_NOT,
                        WithType(type, node.Right));
                }
                break;
        }
        if (node.BinaryOperation is BinaryOperation.BITWISE_AND or BinaryOperation.BITWISE_OR
            && node.Left == node.Right && CanDiscard(node.Left)) {
            return WithType(type, node.Left);
        }
        if (Complement(node) is { } complement) {
            return complement;
        }
        return null;
    }

    /// <summary><c>x &amp; ~x</c> is zero and <c>x | ~x</c> / <c>x ^ ~x</c> is all ones of the
    /// node's width when <c>x</c> is a same-width pure operand.</summary>
    private static ValueNode? Complement(BinaryOperationNode node) {
        if (node.BinaryOperation is not (BinaryOperation.BITWISE_AND or BinaryOperation.BITWISE_OR
            or BinaryOperation.BITWISE_XOR)) {
            return null;
        }
        foreach ((ValueNode maybeNot, ValueNode other) in
            new[] { (node.Left, node.Right), (node.Right, node.Left) }) {
            if (maybeNot is UnaryOperationNode { UnaryOperation: UnaryOperation.BITWISE_NOT,
                    DataType: { } notType } unary
                && notType == node.DataType && unary.Value == other && CanDiscard(other)) {
                return node.BinaryOperation == BinaryOperation.BITWISE_AND
                    ? Const(node.DataType, 0)
                    : Const(node.DataType, MaskOf(node.DataType));
            }
        }
        return null;
    }

    /// <summary>Same-operation constant masks and mask pairs: <c>(x &amp; c1) &amp; c2</c> combines,
    /// <c>(x &amp; c1) | (x &amp; c2)</c> merges the masks, and <c>(x &amp; mask) | constant</c> or
    /// <c>(x | constant) &amp; mask</c> is the constant when it already holds every retained
    /// bit.</summary>
    private static ValueNode? FoldConstantMasks(BinaryOperationNode node) {
        DataType type = node.DataType;
        if (node.BinaryOperation is not (BinaryOperation.BITWISE_AND or BinaryOperation.BITWISE_OR
            or BinaryOperation.BITWISE_XOR) || !IsInteger(type)) {
            return null;
        }
        if (SameWidthShiftOperand(node.Left, type) is BinaryOperationNode
                { BinaryOperation: { } innerOp } inner
            && innerOp == node.BinaryOperation && inner.DataType == type
            && BitsOf(inner.Right, type) is { } first
            && BitsOf(node.Right, type) is { } second) {
            ulong combined = node.BinaryOperation switch {
                BinaryOperation.BITWISE_AND => first & second,
                BinaryOperation.BITWISE_OR => first | second,
                _ => first ^ second
            };
            return FoldBinaryAlgebra(new BinaryOperationNode(type, inner.Left,
                node.BinaryOperation, Const(
                    DataType.UnsignedFromBitWidth(type.BitWidth), combined)));
        }
        if (node.BinaryOperation is BinaryOperation.BITWISE_AND or BinaryOperation.BITWISE_OR) {
            BinaryOperation innerKind = node.BinaryOperation == BinaryOperation.BITWISE_AND
                ? BinaryOperation.BITWISE_OR : BinaryOperation.BITWISE_AND;
            if (MaskedOperand(node.Left, type, innerKind) is { } leftPair
                && MaskedOperand(node.Right, type, innerKind) is { } rightPair
                && leftPair.Operand == rightPair.Operand
                && CanDiscard(leftPair.Operand)) {
                ulong merged = innerKind == BinaryOperation.BITWISE_AND
                    ? leftPair.Constant | rightPair.Constant
                    : leftPair.Constant & rightPair.Constant;
                return FoldBinaryAlgebra(new BinaryOperationNode(type, leftPair.Operand, innerKind,
                    Const(DataType.UnsignedFromBitWidth(type.BitWidth), merged)));
            }
            return ForcedConstant(node, type);
        }
        return null;
    }

    /// <summary><paramref name="node"/> as <c>operand &amp; constant</c> or
    /// <c>operand | constant</c> of the requested operation, the operand seen through same-width
    /// casts.</summary>
    private static (ValueNode Operand, ulong Constant)? MaskedOperand(ValueNode node, DataType type,
        BinaryOperation operation) =>
        SameWidthShiftOperand(node, type) is BinaryOperationNode
            { BinaryOperation: { } op, DataType: { } innerType } inner
            && op == operation && innerType == type
            && BitsOf(inner.Right, type) is { } constant
            ? (inner.Left, constant)
            : null;

    /// <summary><c>(x &amp; mask) | constant</c> is the constant when it holds every bit the mask
    /// retains, and <c>(x | constant) &amp; mask</c> is the mask when the constant covers it; the
    /// dropped operand must be pure.</summary>
    private static ValueNode? ForcedConstant(BinaryOperationNode node, DataType type) {
        foreach ((ValueNode maybeMasked, ValueNode maybeConstant) in
            new[] { (node.Left, node.Right), (node.Right, node.Left) }) {
            if (BitsOf(maybeConstant, type) is not { } constant) {
                continue;
            }
            if (node.BinaryOperation == BinaryOperation.BITWISE_OR
                && MaskedOperand(maybeMasked, type, BinaryOperation.BITWISE_AND) is { } andPair
                && (constant & andPair.Constant) == andPair.Constant
                && CanDiscard(andPair.Operand)) {
                return Const(type, constant);
            }
            if (node.BinaryOperation == BinaryOperation.BITWISE_AND
                && MaskedOperand(maybeMasked, type, BinaryOperation.BITWISE_OR) is { } orPair
                && (constant & orPair.Constant) == constant
                && CanDiscard(orPair.Operand)) {
                return Const(type, constant);
            }
        }
        return null;
    }

    /// <summary><c>(T)y &amp; mask</c> of a wider unsigned type is the conversion alone when the
    /// mask already keeps every bit of <c>y</c>: the AND changes nothing and the operand still
    /// evaluates once.</summary>
    private static ValueNode? FoldKnownWidth(BinaryOperationNode node) {
        DataType type = node.DataType;
        if (node.BinaryOperation != BinaryOperation.BITWISE_AND || !IsInteger(type)) {
            return null;
        }
        foreach ((ValueNode maybeConversion, ValueNode maybeMask) in
            new[] { (node.Left, node.Right), (node.Right, node.Left) }) {
            if (maybeConversion is TypeConversionNode
                    { DataType: { } targetType, Value: { } narrow } conversion
                && targetType == type && !narrow.DataType.Signed
                && MaskOf(narrow.DataType) < MaskOf(type)
                && BitsOf(maybeMask, type) is { } mask
                && (mask & MaskOf(narrow.DataType)) == MaskOf(narrow.DataType)) {
                return conversion;
            }
        }
        return null;
    }

    /// <summary>The constant a shift count resolves to, masked to the CLR's count bits: 5 for
    /// 32-bit and narrower operands, 6 for 64-bit.</summary>
    private static ulong? ShiftCount(ValueNode node, DataType type) =>
        SemanticOf(node) is { } count ? count & (ulong)(type.BitWidth == BitWidth.QWORD_64 ? 63 : 31)
            : null;

    /// <summary>Masks a shift makes redundant: <c>(x &gt;&gt; k) &amp; mask</c> and
    /// <c>(x &lt;&lt; k) &amp; mask</c> keep only the bits the shift can produce, and
    /// <c>(x &amp; mask) &gt;&gt; k</c> drops a mask covering the surviving high bits. Only a
    /// logical (unsigned) right shift qualifies; the mixed
    /// <c>(x &lt;&lt; k) &gt;&gt; k</c> / <c>(x &gt;&gt; k) &lt;&lt; k</c> pairs fold to an explicit
    /// mask so the truncation stays visible.</summary>
    private static ValueNode? FoldShiftMasks(BinaryOperationNode node) {
        DataType type = node.DataType;
        if (!IsInteger(type)) {
            return null;
        }
        ulong widthMask = MaskOf(type);
        if (node.BinaryOperation == BinaryOperation.BITWISE_AND) {
            foreach ((ValueNode maybeShift, ValueNode maybeMask) in
                new[] { (node.Left, node.Right), (node.Right, node.Left) }) {
                if (maybeShift is not BinaryOperationNode { DataType: { } shiftType } shift
                    || shiftType != type
                    || ShiftCount(shift.Right, type) is not { } shiftCount
                    || BitsOf(maybeMask, type) is not { } mask
                    || shift.Left.DataType != type) {
                    continue;
                }
                ulong kept = shift.BinaryOperation switch {
                    BinaryOperation.LEFT_SHIFT => widthMask << (int)shiftCount & widthMask,
                    BinaryOperation.RIGHT_SHIFT when !shiftType.Signed => widthMask >> (int)shiftCount,
                    _ => 0
                };
                if (kept != 0 && (mask & kept) == kept) {
                    return shift;
                }
            }
            return null;
        }
        if (node.BinaryOperation == BinaryOperation.RIGHT_SHIFT && !type.Signed
            && node.Left is BinaryOperationNode { DataType: { } innerType } inner
            && innerType == type && inner.Left.DataType == type
            && ShiftCount(node.Right, type) is { } count) {
            if (inner.BinaryOperation == BinaryOperation.BITWISE_AND
                && BitsOf(inner.Right, type) is { } mask
                && (mask & ((widthMask >> (int)count) << (int)count & widthMask))
                    == ((widthMask >> (int)count) << (int)count & widthMask)) {
                return new BinaryOperationNode(type, inner.Left, BinaryOperation.RIGHT_SHIFT,
                    node.Right);
            }
            if (inner.BinaryOperation == BinaryOperation.LEFT_SHIFT
                && ShiftCount(inner.Right, type) == count) {
                return new BinaryOperationNode(type, inner.Left, BinaryOperation.BITWISE_AND,
                    Const(DataType.UnsignedFromBitWidth(type.BitWidth),
                        widthMask >> (int)count));
            }
        }
        if (node.BinaryOperation == BinaryOperation.LEFT_SHIFT
            && node.Left is BinaryOperationNode { DataType: { } leftInnerType,
                BinaryOperation: BinaryOperation.RIGHT_SHIFT } leftInner
            && leftInnerType == type && !leftInnerType.Signed && leftInner.Left.DataType == type
            && ShiftCount(node.Right, type) is { } leftCount
            && ShiftCount(leftInner.Right, type) == leftCount) {
            return new BinaryOperationNode(type, leftInner.Left, BinaryOperation.BITWISE_AND,
                Const(DataType.UnsignedFromBitWidth(type.BitWidth),
                    widthMask << (int)leftCount & widthMask));
        }
        return null;
    }

    /// <summary><c>(x ^ y) == 0</c> and <c>(x - y) == 0</c> compare <c>x</c> and <c>y</c> directly;
    /// boolean operands compare against the false and true constants or collapse a boolean XOR to
    /// <c>!=</c>.</summary>
    private static ValueNode? FoldEquality(BinaryOperationNode node) {
        if (node.BinaryOperation is BinaryOperation.BITWISE_XOR
            && node.DataType == DataType.BOOL && node.Left.DataType == DataType.BOOL
            && node.Right.DataType == DataType.BOOL) {
            return new BinaryOperationNode(DataType.BOOL, node.Left, BinaryOperation.NOT_EQUAL,
                node.Right);
        }
        if (node.BinaryOperation is not (BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL)) {
            return null;
        }
        if (SemanticOf(node.Right) == 0 && node.Left is BinaryOperationNode
            { BinaryOperation: BinaryOperation.BITWISE_XOR or BinaryOperation.MINUS,
                DataType: { } innerType } inner
            && inner.Left.DataType == inner.Right.DataType
            && innerType == inner.Left.DataType) {
            return new BinaryOperationNode(node.DataType, inner.Left, node.BinaryOperation,
                inner.Right);
        }
        // -x == 0 / != 0 tests x itself, through same-width casts, only when the
        // negation keeps x's width: a narrowed negation tests the truncated value.
        if (SemanticOf(node.Right) == 0) {
            ValueNode unwrapped = node.Left;
            while (unwrapped is TypeConversionNode { Value: { } converted } conversion
                && converted.DataType.BitWidth == conversion.DataType.BitWidth) {
                unwrapped = converted;
            }
            if (unwrapped is UnaryOperationNode
                    { UnaryOperation: UnaryOperation.NEGATE } negation
                && negation.Value.DataType.BitWidth == negation.DataType.BitWidth) {
                return new BinaryOperationNode(node.DataType, negation.Value,
                    node.BinaryOperation, Const(negation.Value.DataType, 0));
            }
        }
        if (node.Left.DataType == DataType.BOOL && SemanticOf(node.Right) is { } constant
            && constant <= 1) {
            bool negate = (node.BinaryOperation == BinaryOperation.EQUAL) == (constant == 0);
            return negate
                ? new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, node.Left)
                : node.Left;
        }
        return null;
    }

    /// <summary><c>a &amp;&amp; a</c> and <c>a || a</c> collapse when the dropped read is pure;
    /// <c>a &amp;&amp; !a</c> is false and <c>a || !a</c> is true when <c>a</c> is pure.</summary>
    private static ValueNode? FoldBoolean(BinaryOperationNode node) {
        if (node.BinaryOperation is not (BinaryOperation.LOGICAL_AND or BinaryOperation.LOGICAL_OR)) {
            return null;
        }
        if (node.Left == node.Right && CanDiscard(node.Left)) {
            return node.Left;
        }
        foreach ((ValueNode maybeNot, ValueNode other) in
            new[] { (node.Left, node.Right), (node.Right, node.Left) }) {
            if (maybeNot is UnaryOperationNode { UnaryOperation: UnaryOperation.NOT } unary
                && unary.Value == other && CanDiscard(other)) {
                return Const(node.DataType,
                    node.BinaryOperation == BinaryOperation.LOGICAL_OR ? 1UL : 0UL);
            }
        }
        return null;
    }

    /// <summary>Unsigned <c>x &lt; 1</c> / <c>x &lt;= 0</c> is <c>x == 0</c> and unsigned
    /// <c>x &gt;= 1</c> / <c>x &gt; 0</c> is <c>x != 0</c>; signed comparisons are never
    /// rewritten.</summary>
    private static ValueNode? FoldUnsignedCompare(BinaryOperationNode node) {
        if (node.Left.DataType.Signed || node.Left.DataType == DataType.BOOL
            || SemanticOf(node.Right) is not { } constant) {
            return null;
        }
        BinaryOperation? equality = (node.BinaryOperation, constant) switch {
            (BinaryOperation.LESS_THAN, 1) or (BinaryOperation.LESS_THAN_OR_EQUAL, 0)
                => BinaryOperation.EQUAL,
            (BinaryOperation.GREATER_THAN, 0) or (BinaryOperation.GREATER_THAN_OR_EQUAL, 1)
                => BinaryOperation.NOT_EQUAL,
            _ => null
        };
        return equality is { } operation
            ? new BinaryOperationNode(node.DataType, node.Left, operation,
                Const(node.Left.DataType, 0))
            : null;
    }
}
