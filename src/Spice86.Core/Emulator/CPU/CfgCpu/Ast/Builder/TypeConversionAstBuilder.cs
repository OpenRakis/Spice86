namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

public class TypeConversionAstBuilder {
    /// <summary>
    /// Creates a type conversion node (cast).
    /// </summary>
    /// <param name="targetType">The target data type</param>
    /// <param name="value">The value to convert</param>
    /// <returns>TypeConversionNode for the cast</returns>
    public TypeConversionNode Convert(DataType targetType, ValueNode value) {
        return new TypeConversionNode(targetType, value);
    }

    /// <summary>
    /// Converts a value to signed type of the same bit width.
    /// UINT8 -> INT8, UINT16 -> INT16, UINT32 -> INT32
    /// </summary>
    /// <param name="value">The unsigned value to convert</param>
    /// <returns>Value converted to signed type</returns>
    public TypeConversionNode ToSigned(ValueNode value) {
        DataType signedType = value.DataType.BitWidth switch {
            BitWidth.BYTE_8 => DataType.INT8,
            BitWidth.WORD_16 => DataType.INT16,
            BitWidth.DWORD_32 => DataType.INT32,
            _ => throw new ArgumentException($"Unsupported bit width: {value.DataType.BitWidth}")
        };
        return new TypeConversionNode(signedType, value);
    }

    /// <summary>
    /// Converts a value to unsigned type of the same bit width.
    /// INT8 -> UINT8, INT16 -> UINT16, INT32 -> UINT32
    /// </summary>
    /// <param name="value">The signed value to convert</param>
    /// <returns>Value converted to unsigned type</returns>
    public TypeConversionNode ToUnsigned(ValueNode value) {
        DataType unsignedType = value.DataType.BitWidth switch {
            BitWidth.BYTE_8 => DataType.UINT8,
            BitWidth.WORD_16 => DataType.UINT16,
            BitWidth.DWORD_32 => DataType.UINT32,
            _ => throw new ArgumentException($"Unsupported bit width: {value.DataType.BitWidth}")
        };
        return new TypeConversionNode(unsignedType, value);
    }
}
