namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes an <c>Alu8</c> / <c>Alu16</c> / <c>Alu32</c> call as plain nodes that compute the same result and assign
/// the same status flags as the emulator's ALU; the operations that read CF get a <c>carry</c> operand. Used by the
/// code generator only; parsers keep the ALU calls.
/// </summary>
internal static class AluExpansionBuilder {
    private const ulong AuxiliaryCarryBit = 0x10;
    private const ushort OddParityByNibble = 0x6996;
    private const ulong ShiftCountMask = 0x1F;

    /// <summary>
    /// The expansion of <paramref name="call"/>, or <c>null</c> when it is not an <c>Add</c>, <c>Sub</c>,
    /// <c>And</c>, <c>Or</c>, <c>Xor</c>, <c>Inc</c>, <c>Dec</c>, <c>Adc</c>, <c>Sbb</c>, a <c>Shl</c>, <c>Shr</c> or
    /// <c>Sar</c> call by a non-zero masked constant count, a <c>Rol</c>, <c>Ror</c>, <c>Rcl</c> or <c>Rcr</c>
    /// call by a constant count that actually rotates, returning the unsigned type of its ALU width, or a
    /// <c>Mul</c> / <c>Imul</c> call returning the type twice as wide as its ALU width.
    /// </summary>
    public static AluExpansion? TryExpand(AluCallValueNode call) {
        BitWidth width = call.Width;
        AluExpansion? multiply = (call.Operation, call.CallNode.Arguments) switch {
            (AluOperation.Mul, [ValueNode left, ValueNode right])
                when call.DataType == DataType.UnsignedFromBitWidth(Double(width)) =>
                ExpandMul(width, left, right),
            (AluOperation.Imul, [ValueNode left, ValueNode right])
                when call.DataType == DataType.SignedFromBitWidth(Double(width)) =>
                ExpandImul(width, left, right),
            _ => null
        };
        if (multiply is not null) {
            return multiply;
        }
        if (call.DataType != DataType.UnsignedFromBitWidth(width)) {
            return null;
        }
        return (call.Operation, call.CallNode.Arguments) switch {
            (AluOperation.Add or AluOperation.Sub or AluOperation.And or AluOperation.Or or AluOperation.Xor,
                    [ValueNode left, ValueNode right]) =>
                ExpandBinary(call.Operation, call.DataType, left, right),
            (AluOperation.Inc or AluOperation.Dec, [ValueNode operand]) =>
                ExpandIncDec(call.Operation == AluOperation.Inc, call.DataType, operand),
            (AluOperation.Shl or AluOperation.Shr or AluOperation.Sar, [ValueNode value, ValueNode count])
                when ConstantCount(count) is ulong constant && (constant & ShiftCountMask) != 0 =>
                ExpandShift(call.Operation, call.DataType, value, count, (int)(constant & ShiftCountMask)),
            (AluOperation.Adc or AluOperation.Sbb, [ValueNode left, ValueNode right]) =>
                ExpandWithCarry(call.Operation == AluOperation.Adc, call.DataType, left, right),
            (AluOperation.Rol or AluOperation.Ror, [ValueNode value, ValueNode count])
                when RotateCount(call.DataType, ConstantCount(count)) is int rotation =>
                ExpandRotate(call.Operation == AluOperation.Rol, call.DataType, value, count, rotation),
            (AluOperation.Rcl or AluOperation.Rcr, [ValueNode value, ValueNode count])
                when RotateThroughCarryCount(call.DataType, ConstantCount(count)) is int rotation =>
                ExpandRotateThroughCarry(call.Operation == AluOperation.Rcl, call.DataType, value, count, rotation),
            _ => null
        };
    }

    /// <summary>
    /// The value of a <c>Shl</c> / <c>Shr</c> / <c>Sar</c> call whose count is not a constant, as plain nodes;
    /// valid only where no flag the call may write is read, because the flags are not written; <c>null</c> for any
    /// other call.
    /// </summary>
    public static ValueNode? TryExpandShiftValue(AluCallValueNode call) {
        if (call.DataType != DataType.UnsignedFromBitWidth(call.Width)
            || call.Operation is not (AluOperation.Shl or AluOperation.Shr or AluOperation.Sar)
            || call.CallNode.Arguments is not [ValueNode value, ValueNode count] || ConstantCount(count) is not null) {
            return null;
        }
        BinaryOperationNode maskedCount = new(count.DataType, count, BinaryOperation.BITWISE_AND,
            new ConstantNode(count.DataType, ShiftCountMask));
        return ShiftValue(call.Operation, call.DataType, value, maskedCount);
    }

    private static BitWidth Double(BitWidth width) => (BitWidth)(2 * (int)width);

    /// <summary>
    /// The expansion of a <c>Mul</c> call: the product of the unsigned operands in the type twice as wide,
    /// CF = OF = the upper half is not zero, ZF = the lower half is zero.
    /// </summary>
    private static AluExpansion ExpandMul(BitWidth width, ValueNode leftValue, ValueNode rightValue) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        DataType doubleType = DataType.UnsignedFromBitWidth(Double(width));
        VariableDeclarationNode left = new(type, "left", leftValue);
        VariableDeclarationNode right = new(type, "right", rightValue);
        VariableDeclarationNode result = new(doubleType, "result", new BinaryOperationNode(doubleType,
            new TypeConversionNode(doubleType, left.Reference), BinaryOperation.MULTIPLY,
            new TypeConversionNode(doubleType, right.Reference)));
        VariableReferenceNode r = result.Reference;
        ulong lowerMask = (1UL << (int)width) - 1;
        ulong upperMask = lowerMask << (int)width;
        List<AluFlagAssignment> flags = [
            new(StatusFlags.Carry, UpperHalfNotZero(r, doubleType, upperMask)),
            new(StatusFlags.Zero, Relation(And(r, new ConstantNode(doubleType, lowerMask)),
                BinaryOperation.EQUAL, new ConstantNode(doubleType, 0))),
            new(StatusFlags.Overflow, UpperHalfNotZero(r, doubleType, upperMask))
        ];
        return new AluExpansion([left, right], result, flags);
    }

    /// <summary>
    /// The expansion of an <c>Imul</c> call: the product of the signed operands in the signed type twice as wide,
    /// CF = OF = the product does not fit in the signed type of the width, PF / ZF / SF from its lower half and
    /// AF clear.
    /// </summary>
    private static AluExpansion ExpandImul(BitWidth width, ValueNode leftValue, ValueNode rightValue) {
        DataType type = DataType.SignedFromBitWidth(width);
        DataType unsignedType = DataType.UnsignedFromBitWidth(width);
        DataType doubleType = DataType.SignedFromBitWidth(Double(width));
        VariableDeclarationNode left = new(type, "left", leftValue);
        VariableDeclarationNode right = new(type, "right", rightValue);
        VariableDeclarationNode result = new(doubleType, "result", new BinaryOperationNode(doubleType,
            new TypeConversionNode(doubleType, left.Reference), BinaryOperation.MULTIPLY,
            new TypeConversionNode(doubleType, right.Reference)));
        VariableReferenceNode r = result.Reference;
        TypeConversionNode low = new(unsignedType, r);
        List<AluFlagAssignment> flags = [
            new(StatusFlags.Carry, TruncationLosses(r, type, doubleType)),
            new(StatusFlags.Parity, EvenParity(low)),
            new(StatusFlags.Auxiliary, False()),
            new(StatusFlags.Zero, IsZero(low)),
            new(StatusFlags.Sign, IsNegative(low)),
            new(StatusFlags.Overflow, TruncationLosses(r, type, doubleType))
        ];
        return new AluExpansion([left, right], result, flags);
    }

    private static BinaryOperationNode UpperHalfNotZero(VariableReferenceNode r, DataType doubleType, ulong mask) =>
        Relation(And(r, new ConstantNode(doubleType, mask)), BinaryOperation.NOT_EQUAL,
            new ConstantNode(doubleType, 0));

    private static BinaryOperationNode TruncationLosses(VariableReferenceNode r, DataType type, DataType doubleType) =>
        Relation(r, BinaryOperation.NOT_EQUAL,
            new TypeConversionNode(doubleType, new TypeConversionNode(type, r)));

    private static ulong? ConstantCount(ValueNode count) => count switch {
        ConstantNode constant => constant.Value,
        InstructionFieldNode { ResolvedNode: ConstantNode resolved } => resolved.Value,
        _ => null
    };

    private static AluExpansion ExpandBinary(AluOperation operation, DataType type, ValueNode leftValue, ValueNode rightValue) {
        VariableDeclarationNode left = new(type, "left", leftValue);
        VariableDeclarationNode right = new(type, "right", rightValue);
        VariableReferenceNode a = left.Reference;
        VariableReferenceNode b = right.Reference;
        BinaryOperation resultOperation = operation switch {
            AluOperation.Add => BinaryOperation.PLUS,
            AluOperation.Sub => BinaryOperation.MINUS,
            AluOperation.And => BinaryOperation.BITWISE_AND,
            AluOperation.Or => BinaryOperation.BITWISE_OR,
            _ => BinaryOperation.BITWISE_XOR
        };
        VariableDeclarationNode result = new(type, "result", new BinaryOperationNode(type, a, resultOperation, b));
        VariableReferenceNode r = result.Reference;
        List<AluFlagAssignment> flags = operation switch {
            AluOperation.Add => [
                new(StatusFlags.Carry, Relation(r, BinaryOperation.LESS_THAN, a)),
                ..AddSubFlags(false, type, a, b, r)
            ],
            AluOperation.Sub => [
                new(StatusFlags.Carry, Relation(a, BinaryOperation.LESS_THAN, b)),
                ..AddSubFlags(true, type, a, b, r)
            ],
            _ => [
                new(StatusFlags.Carry, False()),
                new(StatusFlags.Parity, EvenParity(r)),
                new(StatusFlags.Auxiliary, False()),
                new(StatusFlags.Zero, IsZero(r)),
                new(StatusFlags.Sign, IsNegative(r)),
                new(StatusFlags.Overflow, False())
            ]
        };
        return new AluExpansion([left, right], result, flags);
    }

    private static AluExpansion ExpandIncDec(bool increment, DataType type, ValueNode operandValue) {
        VariableDeclarationNode left = new(type, "left", operandValue);
        VariableReferenceNode a = left.Reference;
        BinaryOperation resultOperation = increment ? BinaryOperation.PLUS : BinaryOperation.MINUS;
        VariableDeclarationNode result = new(type, "result",
            new BinaryOperationNode(type, a, resultOperation, new ConstantNode(type, 1)));
        VariableReferenceNode r = result.Reference;
        ulong overflowResult = increment ? SignBit(type) : SignBit(type) - 1;
        List<AluFlagAssignment> flags = [
            new(StatusFlags.Parity, EvenParity(r)),
            new(StatusFlags.Auxiliary, BitSet(Xor(a, r), AuxiliaryCarryBit)),
            new(StatusFlags.Zero, IsZero(r)),
            new(StatusFlags.Sign, IsNegative(r)),
            new(StatusFlags.Overflow, Relation(r, BinaryOperation.EQUAL, new ConstantNode(type, overflowResult)))
        ];
        return new AluExpansion([left], result, flags);
    }

    private static BinaryOperationNode EvenParity(ValueNode value) {
        ValueNode low = value.DataType == DataType.UINT8 ? value : new TypeConversionNode(DataType.UINT8, value);
        BinaryOperationNode folded = Xor(low,
            new BinaryOperationNode(DataType.UINT8, low, BinaryOperation.RIGHT_SHIFT, new ConstantNode(DataType.UINT8, 4)));
        BinaryOperationNode nibble = And(folded, new ConstantNode(DataType.UINT8, 0x0F));
        BinaryOperationNode oddParity = And(
            new BinaryOperationNode(DataType.UINT16, new ConstantNode(DataType.UINT16, OddParityByNibble),
                BinaryOperation.RIGHT_SHIFT, nibble),
            new ConstantNode(DataType.UINT16, 1));
        return IsZero(oddParity);
    }

    private static BinaryOperationNode IsZero(ValueNode value) =>
        Relation(value, BinaryOperation.EQUAL, new ConstantNode(value.DataType, 0));

    private static BinaryOperationNode IsNegative(ValueNode value) {
        DataType signedType = DataType.SignedFromBitWidth(value.DataType.BitWidth);
        return Relation(new TypeConversionNode(signedType, value), BinaryOperation.LESS_THAN, new ConstantNode(signedType, 0));
    }

    private static BinaryOperationNode BitSet(ValueNode value, ulong mask) =>
        Relation(And(value, new ConstantNode(value.DataType, mask)), BinaryOperation.NOT_EQUAL,
            new ConstantNode(value.DataType, 0));

    private static BinaryOperationNode Xor(ValueNode left, ValueNode right) =>
        new(left.DataType, left, BinaryOperation.BITWISE_XOR, right);

    private static BinaryOperationNode And(ValueNode left, ValueNode right) =>
        new(left.DataType, left, BinaryOperation.BITWISE_AND, right);

    private static BinaryOperationNode Relation(ValueNode left, BinaryOperation operation, ValueNode right) =>
        new(DataType.BOOL, left, operation, right);

    private static ConstantNode False() => new(DataType.BOOL, 0);

    private static ulong SignBit(DataType type) => 1UL << ((int)type.BitWidth - 1);

    /// <summary>
    /// The expansion of a <c>Shl</c> / <c>Shr</c> / <c>Sar</c> call by a non-zero masked constant count.
    /// </summary>
    private static AluExpansion ExpandShift(AluOperation operation, DataType type, ValueNode valueArgument,
        ValueNode countArgument, int count) {
        VariableDeclarationNode left = new(type, "left", valueArgument);
        VariableDeclarationNode right = new(countArgument.DataType, "right", countArgument);
        VariableReferenceNode a = left.Reference;
        VariableDeclarationNode result = new(type, "result",
            ShiftValue(operation, type, a, new ConstantNode(DataType.INT32, (ulong)count)));
        VariableReferenceNode r = result.Reference;
        ValueNode carry = ShiftCarry(operation, type, a, count);
        ValueNode overflow = operation switch {
            AluOperation.Shl => Relation(IsNegative(r), BinaryOperation.NOT_EQUAL, carry),
            AluOperation.Shr when count == 1 => IsNegative(a),
            _ => False()
        };
        List<AluFlagAssignment> flags = [
            new(StatusFlags.Carry, carry),
            new(StatusFlags.Parity, EvenParity(r)),
            new(StatusFlags.Zero, IsZero(r)),
            new(StatusFlags.Sign, IsNegative(r)),
            new(StatusFlags.Overflow, overflow)
        ];
        return new AluExpansion([left, right], result, flags);
    }

    /// <summary>
    /// The value of a <c>Shl</c> / <c>Shr</c> / <c>Sar</c> call as plain nodes: the <c>Sar</c> operand is shifted
    /// as the signed type and converted back to the unsigned width; other shifts stay on the operand type.
    /// </summary>
    private static ValueNode ShiftValue(AluOperation operation, DataType type, ValueNode value, ValueNode count) {
        if (operation == AluOperation.Sar) {
            DataType signedType = DataType.SignedFromBitWidth(type.BitWidth);
            return new TypeConversionNode(type, new BinaryOperationNode(signedType,
                new TypeConversionNode(signedType, value), BinaryOperation.RIGHT_SHIFT, count));
        }
        BinaryOperation shift = operation == AluOperation.Shl ? BinaryOperation.LEFT_SHIFT : BinaryOperation.RIGHT_SHIFT;
        return new BinaryOperationNode(type, value, shift, count);
    }

    /// <summary>
    /// The carry of a constant-count shift: <c>Sar</c> reads bit <c>min(count, width) - 1</c> (the sign bit when the
    /// count reaches the width); <c>Shl</c> reads bit <c>width - count</c> and <c>Shr</c> bit <c>count - 1</c>; for
    /// an 8-bit count above the width the bit shifted out exists only when the count is a multiple of the width
    /// (bit 0 for <c>Shl</c>, the sign bit for <c>Shr</c>), and a 16-bit count above the width loses the carry.
    /// </summary>
    private static ValueNode ShiftCarry(AluOperation operation, DataType type, ValueNode value, int count) {
        int width = (int)type.BitWidth;
        if (operation == AluOperation.Sar) {
            return BitSet(value, 1UL << Math.Min(count - 1, width - 1));
        }
        bool shl = operation == AluOperation.Shl;
        if (width == 8 && count > 8) {
            return (count & 7) == 0 ? BitSet(value, shl ? 0x01UL : 0x80UL) : False();
        }
        if (width == 16 && count > 16) {
            return False();
        }
        return BitSet(value, 1UL << (shl ? width - count : count - 1));
    }

    /// <summary>
    /// The parity, auxiliary, zero, sign and overflow assignments an <c>Add</c>- or <c>Sub</c>-like call writes;
    /// shared by <c>Add</c> / <c>Sub</c> and the carry-in forms <c>Adc</c> / <c>Sbb</c>.
    /// </summary>
    private static List<AluFlagAssignment> AddSubFlags(bool subtract, DataType type,
        VariableReferenceNode a, VariableReferenceNode b, VariableReferenceNode r) => [
        new(StatusFlags.Parity, EvenParity(r)),
        new(StatusFlags.Auxiliary, BitSet(Xor(Xor(a, b), r), AuxiliaryCarryBit)),
        new(StatusFlags.Zero, IsZero(r)),
        new(StatusFlags.Sign, IsNegative(r)),
        new(StatusFlags.Overflow, subtract
            ? BitSet(And(Xor(a, b), Xor(a, r)), SignBit(type))
            : BitSet(And(Xor(a, r), Xor(b, r)), SignBit(type)))
    ];

    /// <summary>
    /// The expansion of an <c>Adc</c> or <c>Sbb</c> call: the old carry flag is a third operand, as a number in the
    /// value and as a flag in the carry-out formula; all six status flags are written.
    /// </summary>
    private static AluExpansion ExpandWithCarry(bool add, DataType type, ValueNode leftValue, ValueNode rightValue) {
        VariableDeclarationNode left = new(type, "left", leftValue);
        VariableDeclarationNode right = new(type, "right", rightValue);
        VariableDeclarationNode carry = new(DataType.BOOL, "carry", new CpuFlagNode((ushort)StatusFlags.Carry));
        VariableReferenceNode a = left.Reference;
        VariableReferenceNode b = right.Reference;
        VariableReferenceNode cf = carry.Reference;
        ValueNode carryNumber = new TypeConversionNode(type, cf);
        BinaryOperation operation = add ? BinaryOperation.PLUS : BinaryOperation.MINUS;
        VariableDeclarationNode result = new(type, "result",
            new BinaryOperationNode(type, new BinaryOperationNode(type, a, operation, b), operation, carryNumber));
        VariableReferenceNode r = result.Reference;
        ValueNode carryOut = add
            ? LogicalOr(Relation(r, BinaryOperation.LESS_THAN, a),
                LogicalAnd(cf, Relation(r, BinaryOperation.EQUAL, a)))
            : LogicalOr(Relation(a, BinaryOperation.LESS_THAN, b),
                LogicalAnd(cf, Relation(a, BinaryOperation.EQUAL, b)));
        List<AluFlagAssignment> flags = [
            new(StatusFlags.Carry, carryOut),
            ..AddSubFlags(!add, type, a, b, r)
        ];
        return new AluExpansion([left, right, carry], result, flags);
    }

    /// <summary>
    /// The expansion of a <c>Rol</c> or <c>Ror</c> call by <paramref name="rotation"/> bits, a non-zero remainder of
    /// the masked count modulo the width; CF and OF are written.
    /// </summary>
    private static AluExpansion ExpandRotate(bool rotateLeft, DataType type, ValueNode value, ValueNode count,
        int rotation) {
        VariableDeclarationNode left = new(type, "left", value);
        VariableDeclarationNode right = new(count.DataType, "right", count);
        VariableReferenceNode a = left.Reference;
        int width = (int)type.BitWidth;
        BinaryOperation first = rotateLeft ? BinaryOperation.LEFT_SHIFT : BinaryOperation.RIGHT_SHIFT;
        BinaryOperation second = rotateLeft ? BinaryOperation.RIGHT_SHIFT : BinaryOperation.LEFT_SHIFT;
        VariableDeclarationNode result = new(type, "result", new BinaryOperationNode(type,
            new BinaryOperationNode(type, a, first, new ConstantNode(DataType.INT32, (ulong)rotation)),
            BinaryOperation.BITWISE_OR,
            new BinaryOperationNode(type, a, second, new ConstantNode(DataType.INT32, (ulong)(width - rotation)))));
        VariableReferenceNode r = result.Reference;
        ValueNode carry = rotateLeft ? BitSet(r, 1) : IsNegative(r);
        ValueNode otherBit = rotateLeft ? BitSet(r, 1) : BitSet(r, 1UL << (width - 2));
        ValueNode overflow = Relation(IsNegative(r), BinaryOperation.NOT_EQUAL, otherBit);
        List<AluFlagAssignment> flags = [
            new(StatusFlags.Carry, carry),
            new(StatusFlags.Overflow, overflow)
        ];
        return new AluExpansion([left, right], result, flags);
    }

    /// <summary>
    /// The expansion of a <c>Rcl</c> or <c>Rcr</c> call by <paramref name="rotation"/> bits through the carry: the old
    /// carry flag is a third operand, a number inside the rotated value; CF and OF are written.
    /// </summary>
    private static AluExpansion ExpandRotateThroughCarry(bool rotateLeft, DataType type, ValueNode value,
        ValueNode count, int rotation) {
        VariableDeclarationNode left = new(type, "left", value);
        VariableDeclarationNode right = new(count.DataType, "right", count);
        VariableDeclarationNode carry = new(DataType.BOOL, "carry", new CpuFlagNode((ushort)StatusFlags.Carry));
        VariableReferenceNode a = left.Reference;
        VariableReferenceNode cf = carry.Reference;
        int width = (int)type.BitWidth;
        ValueNode carryNumber = new TypeConversionNode(type, cf);
        ValueNode operandShift = rotateLeft
            ? new BinaryOperationNode(type, a, BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, (ulong)rotation))
            : new BinaryOperationNode(type, a, BinaryOperation.RIGHT_SHIFT, new ConstantNode(DataType.INT32, (ulong)rotation));
        int carryShift = rotateLeft ? rotation - 1 : width - rotation;
        ValueNode carryShifted = carryShift == 0
            ? carryNumber
            : new BinaryOperationNode(type, carryNumber, BinaryOperation.LEFT_SHIFT,
                new ConstantNode(DataType.INT32, (ulong)carryShift));
        ValueNode resultValue = new BinaryOperationNode(type, operandShift, BinaryOperation.BITWISE_OR, carryShifted);
        if (rotation > 1) {
            ValueNode wrapped = rotateLeft
                ? new BinaryOperationNode(type, a, BinaryOperation.RIGHT_SHIFT,
                    new ConstantNode(DataType.INT32, (ulong)(width + 1 - rotation)))
                : new BinaryOperationNode(type, a, BinaryOperation.LEFT_SHIFT,
                    new ConstantNode(DataType.INT32, (ulong)(width + 1 - rotation)));
            resultValue = new BinaryOperationNode(type, resultValue, BinaryOperation.BITWISE_OR, wrapped);
        }
        VariableDeclarationNode result = new(type, "result", resultValue);
        VariableReferenceNode r = result.Reference;
        ValueNode newCarry = rotateLeft ? BitSet(a, 1UL << (width - rotation)) : BitSet(a, 1UL << (rotation - 1));
        ValueNode overflow = rotateLeft
            ? Relation(newCarry, BinaryOperation.NOT_EQUAL, IsNegative(r))
            : Relation(IsNegative(r), BinaryOperation.NOT_EQUAL, BitSet(r, 1UL << (width - 2)));
        List<AluFlagAssignment> flags = [
            new(StatusFlags.Carry, newCarry),
            new(StatusFlags.Overflow, overflow)
        ];
        return new AluExpansion([left, right, carry], result, flags);
    }

    /// <summary>
    /// The rotation of a <c>Rol</c> / <c>Ror</c> count: the masked count modulo the width, or <c>null</c> when there
    /// is no constant count or the rotation is a multiple of the width (the value comes back unchanged).
    /// </summary>
    private static int? RotateCount(DataType type, ulong? count) {
        if (count is null) {
            return null;
        }
        int masked = (int)(count.Value & ShiftCountMask);
        if (masked == 0) {
            return null;
        }
        int rotation = masked % (int)type.BitWidth;
        return rotation == 0 ? null : rotation;
    }

    /// <summary>
    /// The rotation of a <c>Rcl</c> / <c>Rcr</c> count: the masked count modulo the width plus one (the count itself
    /// for 32 bits), or <c>null</c> when there is no constant count or no carry moves through the value.
    /// </summary>
    private static int? RotateThroughCarryCount(DataType type, ulong? count) {
        if (count is null) {
            return null;
        }
        int width = (int)type.BitWidth;
        int masked = (int)(count.Value & ShiftCountMask);
        if (masked == 0) {
            return null;
        }
        int rotation = width == 32 ? masked : masked % (width + 1);
        return rotation == 0 ? null : rotation;
    }

    private static BinaryOperationNode LogicalOr(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.LOGICAL_OR, right);

    private static BinaryOperationNode LogicalAnd(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.LOGICAL_AND, right);
}
