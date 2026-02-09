namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

public class TypeConversionAstBuilder {
    /// <summary>
    /// Creates a type conversion node (cast).
    /// </summary>
    /// <param name="targetType">The target data type</param>
    /// <param name="value">The value to convert</param>
    /// <returns>TypeConversionNode for the cast, or the original value if no conversion is needed</returns>
    public ValueNode Convert(DataType targetType, ValueNode value) {
        // Avoid creating unnecessary conversion node if types already match
        if (value.DataType == targetType) {
            return value;
        }

        if (value is ConstantNode constant) {
            // For constants we can compute directly the new constant node
            ulong newValue = constant.Convert(targetType);
            return new ConstantNode(targetType, newValue);
        }
        return new TypeConversionNode(targetType, value);
    }

    /// <summary>
    /// Converts a value to signed type of the same bit width.
    /// </summary>
    /// <param name="value">The unsigned value to convert</param>
    /// <returns>Value converted to signed type, or the original value if already signed</returns>
    public ValueNode ToSigned(ValueNode value) {
        DataType signedType = value.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => DataType.INT4,
            BitWidth.QUIBBLE_5 => DataType.INT5,
            BitWidth.BYTE_8 => DataType.INT8,
            BitWidth.WORD_16 => DataType.INT16,
            BitWidth.DWORD_32 => DataType.INT32,
            BitWidth.QWORD_64 => DataType.INT64,
            _ => throw new ArgumentException($"Unsupported bit width: {value.DataType.BitWidth}")
        };
        return Convert(signedType, value);
    }

    /// <summary>
    /// Converts a value to unsigned type of the same bit width.
    /// </summary>
    /// <param name="value">The signed value to convert</param>
    /// <returns>Value converted to unsigned type, or the original value if already unsigned</returns>
    public ValueNode ToUnsigned(ValueNode value) {
        DataType unsignedType = value.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => DataType.UINT4,
            BitWidth.QUIBBLE_5 => DataType.UINT5,
            BitWidth.BYTE_8 => DataType.UINT8,
            BitWidth.WORD_16 => DataType.UINT16,
            BitWidth.DWORD_32 => DataType.UINT32,
            BitWidth.QWORD_64 => DataType.UINT64,
            _ => throw new ArgumentException($"Unsupported bit width: {value.DataType.BitWidth}")
        };
        return Convert(unsignedType, value);
    }
}
