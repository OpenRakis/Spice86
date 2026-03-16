namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// Helper class for creating bitwise operation nodes in the AST.
/// </summary>
public class BitwiseAstBuilder {
    private readonly ConstantAstBuilder _constant;

    public BitwiseAstBuilder(ConstantAstBuilder constant) {
        _constant = constant;
    }

    /// <summary>
    /// Creates an AST node that tests if a bit is set in a value.
    /// Returns a boolean node: (value &amp; (1 &lt;&lt; bitIndex)) != 0
    /// </summary>
    public BinaryOperationNode IsBitSet(ValueNode value, ValueNode bitIndex) {
        DataType dataType = value.DataType;

        // mask = 1 << bitIndex
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(dataType, 1), BinaryOperation.LEFT_SHIFT, bitIndex);

        // value & mask
        BinaryOperationNode andOp = new BinaryOperationNode(dataType, value, BinaryOperation.BITWISE_AND, mask);

        // (value & mask) != 0
        return new BinaryOperationNode(DataType.BOOL, andOp, BinaryOperation.NOT_EQUAL, _constant.ToNode(dataType, 0));
    }

    /// <summary>
    /// Creates an AST node that sets a bit in a value.
    /// Returns: value | (1 &lt;&lt; bitIndex)
    /// </summary>
    public BinaryOperationNode SetBit(ValueNode value, ValueNode bitIndex) {
        DataType dataType = value.DataType;

        // mask = 1 << bitIndex
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(dataType, 1), BinaryOperation.LEFT_SHIFT, bitIndex);

        // value | mask
        return new BinaryOperationNode(dataType, value, BinaryOperation.BITWISE_OR, mask);
    }

    /// <summary>
    /// Creates an AST node that resets (clears) a bit in a value.
    /// Returns: value &amp; ~(1 &lt;&lt; bitIndex)
    /// </summary>
    public BinaryOperationNode ResetBit(ValueNode value, ValueNode bitIndex) {
        DataType dataType = value.DataType;

        // mask = 1 << bitIndex
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(dataType, 1), BinaryOperation.LEFT_SHIFT, bitIndex);

        // ~mask
        UnaryOperationNode notMask = new UnaryOperationNode(dataType, UnaryOperation.BITWISE_NOT, mask);

        // value & ~mask
        return new BinaryOperationNode(dataType, value, BinaryOperation.BITWISE_AND, notMask);
    }

    /// <summary>
    /// Creates an AST node that toggles (complements) a bit in a value.
    /// Returns: value ^ (1 &lt;&lt; bitIndex)
    /// </summary>
    public BinaryOperationNode ToggleBit(ValueNode value, ValueNode bitIndex) {
        DataType dataType = value.DataType;

        // mask = 1 << bitIndex
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(dataType, 1), BinaryOperation.LEFT_SHIFT, bitIndex);

        // value ^ mask
        return new BinaryOperationNode(dataType, value, BinaryOperation.BITWISE_XOR, mask);
    }

    /// <summary>
    /// Creates an AST node that performs a 32-bit byte-swap (BSWAP) on a value.
    /// Returns: (v &gt;&gt; 24) | ((v &gt;&gt; 8) &amp; 0x0000FF00) | ((v &lt;&lt; 8) &amp; 0x00FF0000) | (v &lt;&lt; 24)
    /// </summary>
    /// <param name="value">The 32-bit value to byte-swap. May be read multiple times in the expression; use a variable reference for side-effectful sources.</param>
    public BinaryOperationNode ByteSwap(ValueNode value) {
        DataType dataType = value.DataType;
        BinaryOperationNode shiftRight24 = new BinaryOperationNode(dataType, value, BinaryOperation.RIGHT_SHIFT, _constant.ToNode(24));
        BinaryOperationNode shiftRight8AndMask = new BinaryOperationNode(dataType,
            new BinaryOperationNode(dataType, value, BinaryOperation.RIGHT_SHIFT, _constant.ToNode(8)),
            BinaryOperation.BITWISE_AND, _constant.ToNode(0x0000FF00u));
        BinaryOperationNode shiftLeft8AndMask = new BinaryOperationNode(dataType,
            new BinaryOperationNode(dataType, value, BinaryOperation.LEFT_SHIFT, _constant.ToNode(8)),
            BinaryOperation.BITWISE_AND, _constant.ToNode(0x00FF0000u));
        BinaryOperationNode shiftLeft24 = new BinaryOperationNode(dataType, value, BinaryOperation.LEFT_SHIFT, _constant.ToNode(24));
        return new BinaryOperationNode(dataType,
            new BinaryOperationNode(dataType,
                new BinaryOperationNode(dataType, shiftRight24, BinaryOperation.BITWISE_OR, shiftRight8AndMask),
                BinaryOperation.BITWISE_OR, shiftLeft8AndMask),
            BinaryOperation.BITWISE_OR, shiftLeft24);
    }
}
