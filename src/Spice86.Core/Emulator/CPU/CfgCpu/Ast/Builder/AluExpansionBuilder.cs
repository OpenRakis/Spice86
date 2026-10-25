namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes an <c>Alu8</c> / <c>Alu16</c> / <c>Alu32</c> call as plain nodes that compute the same result and assign
/// the same status flags as the emulator's ALU. Used by the code generator only; parsers keep the ALU calls.
/// </summary>
internal static class AluExpansionBuilder {
    private const ulong AuxiliaryCarryBit = 0x10;
    private const ushort OddParityByNibble = 0x6996;
    private const ulong ShiftCountMask = 0x1F;

    /// <summary>
    /// The expansion of <paramref name="call"/>, or <c>null</c> when it is not an <c>Add</c>, <c>Sub</c>,
    /// <c>And</c>, <c>Or</c>, <c>Xor</c>, <c>Inc</c>, <c>Dec</c>, or a <c>Shl</c>, <c>Shr</c> or <c>Sar</c> call by a
    /// non-zero masked constant count returning the unsigned type of its ALU width.
    /// </summary>
    public static AluExpansion? TryExpand(MethodCallValueNode call) {
        MethodCallNode callNode = call.CallNode;
        BitWidth? width = AluWidth(call);
        if (width is null || call.DataType != DataType.UnsignedFromBitWidth(width.Value)) {
            return null;
        }
        return (callNode.MethodName, callNode.Arguments) switch {
            ("Add" or "Sub" or "And" or "Or" or "Xor", [ValueNode left, ValueNode right]) =>
                ExpandBinary(callNode.MethodName, call.DataType, left, right),
            ("Inc" or "Dec", [ValueNode operand]) => ExpandIncDec(callNode.MethodName == "Inc", call.DataType, operand),
            ("Shl" or "Shr" or "Sar", [ValueNode value, ValueNode count])
                when ConstantCount(count) is ulong constant && (constant & ShiftCountMask) != 0 =>
                ExpandShift(callNode.MethodName, call.DataType, value, count, (int)(constant & ShiftCountMask)),
            _ => null
        };
    }

    /// <summary>
    /// The value of a <c>Shl</c> / <c>Shr</c> / <c>Sar</c> call whose count is not a constant, as plain nodes;
    /// valid only where no flag the call may write is read, because the flags are not written; <c>null</c> for any
    /// other call.
    /// </summary>
    public static ValueNode? TryExpandShiftValue(MethodCallValueNode call) {
        MethodCallNode callNode = call.CallNode;
        BitWidth? width = AluWidth(call);
        if (width is null || call.DataType != DataType.UnsignedFromBitWidth(width.Value)
            || callNode.MethodName is not ("Shl" or "Shr" or "Sar")
            || callNode.Arguments is not [ValueNode value, ValueNode count] || ConstantCount(count) is not null) {
            return null;
        }
        BinaryOperationNode maskedCount = new(count.DataType, count, BinaryOperation.BITWISE_AND,
            new ConstantNode(count.DataType, ShiftCountMask));
        return ShiftValue(callNode.MethodName, call.DataType, value, maskedCount);
    }

    private static BitWidth? AluWidth(MethodCallValueNode call) => call.CallNode.PropertyPath switch {
        "Alu8" => BitWidth.BYTE_8,
        "Alu16" => BitWidth.WORD_16,
        "Alu32" => BitWidth.DWORD_32,
        _ => null
    };

    private static ulong? ConstantCount(ValueNode count) => count switch {
        ConstantNode constant => constant.Value,
        InstructionFieldNode { ResolvedNode: ConstantNode resolved } => resolved.Value,
        _ => null
    };

    private static AluExpansion ExpandBinary(string operation, DataType type, ValueNode leftValue, ValueNode rightValue) {
        VariableDeclarationNode left = new(type, "left", leftValue);
        VariableDeclarationNode right = new(type, "right", rightValue);
        VariableReferenceNode a = left.Reference;
        VariableReferenceNode b = right.Reference;
        BinaryOperation resultOperation = operation switch {
            "Add" => BinaryOperation.PLUS,
            "Sub" => BinaryOperation.MINUS,
            "And" => BinaryOperation.BITWISE_AND,
            "Or" => BinaryOperation.BITWISE_OR,
            _ => BinaryOperation.BITWISE_XOR
        };
        VariableDeclarationNode result = new(type, "result", new BinaryOperationNode(type, a, resultOperation, b));
        VariableReferenceNode r = result.Reference;
        List<AluFlagAssignment> flags = operation switch {
            "Add" => [
                new(StatusFlags.Carry, Relation(r, BinaryOperation.LESS_THAN, a)),
                new(StatusFlags.Parity, EvenParity(r)),
                new(StatusFlags.Auxiliary, BitSet(Xor(Xor(a, b), r), AuxiliaryCarryBit)),
                new(StatusFlags.Zero, IsZero(r)),
                new(StatusFlags.Sign, IsNegative(r)),
                new(StatusFlags.Overflow, BitSet(And(Xor(a, r), Xor(b, r)), SignBit(type)))
            ],
            "Sub" => [
                new(StatusFlags.Carry, Relation(a, BinaryOperation.LESS_THAN, b)),
                new(StatusFlags.Parity, EvenParity(r)),
                new(StatusFlags.Auxiliary, BitSet(Xor(Xor(a, b), r), AuxiliaryCarryBit)),
                new(StatusFlags.Zero, IsZero(r)),
                new(StatusFlags.Sign, IsNegative(r)),
                new(StatusFlags.Overflow, BitSet(And(Xor(a, b), Xor(a, r)), SignBit(type)))
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
    private static AluExpansion ExpandShift(string operation, DataType type, ValueNode valueArgument,
        ValueNode countArgument, int count) {
        VariableDeclarationNode left = new(type, "left", valueArgument);
        VariableDeclarationNode right = new(countArgument.DataType, "right", countArgument);
        VariableReferenceNode a = left.Reference;
        VariableDeclarationNode result = new(type, "result",
            ShiftValue(operation, type, a, new ConstantNode(DataType.INT32, (ulong)count)));
        VariableReferenceNode r = result.Reference;
        ValueNode carry = ShiftCarry(operation, type, a, count);
        ValueNode overflow = operation switch {
            "Shl" => Relation(IsNegative(r), BinaryOperation.NOT_EQUAL, carry),
            "Shr" when count == 1 => IsNegative(a),
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
    private static ValueNode ShiftValue(string operation, DataType type, ValueNode value, ValueNode count) {
        if (operation == "Sar") {
            DataType signedType = DataType.SignedFromBitWidth(type.BitWidth);
            return new TypeConversionNode(type, new BinaryOperationNode(signedType,
                new TypeConversionNode(signedType, value), BinaryOperation.RIGHT_SHIFT, count));
        }
        BinaryOperation shift = operation == "Shl" ? BinaryOperation.LEFT_SHIFT : BinaryOperation.RIGHT_SHIFT;
        return new BinaryOperationNode(type, value, shift, count);
    }

    /// <summary>
    /// The carry of a constant-count shift: <c>Sar</c> reads bit <c>min(count, width) - 1</c> (the sign bit when the
    /// count reaches the width); <c>Shl</c> reads bit <c>width - count</c> and <c>Shr</c> bit <c>count - 1</c>; for
    /// an 8-bit count above the width the bit shifted out exists only when the count is a multiple of the width
    /// (bit 0 for <c>Shl</c>, the sign bit for <c>Shr</c>), and a 16-bit count above the width loses the carry.
    /// </summary>
    private static ValueNode ShiftCarry(string operation, DataType type, ValueNode value, int count) {
        int width = (int)type.BitWidth;
        if (operation == "Sar") {
            return BitSet(value, 1UL << Math.Min(count - 1, width - 1));
        }
        bool shl = operation == "Shl";
        if (width == 8 && count > 8) {
            return (count & 7) == 0 ? BitSet(value, shl ? 0x01UL : 0x80UL) : False();
        }
        if (width == 16 && count > 16) {
            return False();
        }
        return BitSet(value, 1UL << (shl ? width - count : count - 1));
    }
}
