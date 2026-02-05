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
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(1), BinaryOperation.LEFT_SHIFT, bitIndex);

        // value & mask
        BinaryOperationNode andOp = new BinaryOperationNode(dataType, value, BinaryOperation.BITWISE_AND, mask);

        // (value & mask) != 0
        return new BinaryOperationNode(DataType.BOOL, andOp, BinaryOperation.NOT_EQUAL, _constant.ToNode(0));
    }

    /// <summary>
    /// Creates an AST node that sets a bit in a value.
    /// Returns: value | (1 &lt;&lt; bitIndex)
    /// </summary>
    public BinaryOperationNode SetBit(ValueNode value, ValueNode bitIndex) {
        DataType dataType = value.DataType;

        // mask = 1 << bitIndex
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(1), BinaryOperation.LEFT_SHIFT, bitIndex);

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
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(1), BinaryOperation.LEFT_SHIFT, bitIndex);

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
        BinaryOperationNode mask = new BinaryOperationNode(dataType, _constant.ToNode(1), BinaryOperation.LEFT_SHIFT, bitIndex);

        // value ^ mask
        return new BinaryOperationNode(dataType, value, BinaryOperation.BITWISE_XOR, mask);
    }
}
