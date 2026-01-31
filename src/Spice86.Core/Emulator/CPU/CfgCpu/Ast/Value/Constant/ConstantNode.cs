namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

public record ConstantNode(DataType DataType, ulong Value) : ValueNode(DataType) {

    public long SignedValue =>
        DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => (sbyte)ConvertUtils.SignExtend(Value, 3),
            BitWidth.QUIBBLE_5 => (sbyte)ConvertUtils.SignExtend(Value, 4),
            BitWidth.BYTE_8 => (sbyte)Value,
            BitWidth.WORD_16 => (short)Value,
            BitWidth.DWORD_32 => (int)Value,
            BitWidth.QWORD_64 => (long)Value,
            _ => throw new InvalidOperationException($"Unsupported bit width {DataType.BitWidth}")
        };

    public bool IsNegative {
        get {
            if (!DataType.Signed) {
                return false;
            }

            return SignedValue < 0;
        }
    }

    public ulong Convert(DataType targetType) {
        if (DataType.Signed && DataType.BitWidth < targetType.BitWidth && targetType.Signed) {
            // Sign extend
            return (ulong)SignedValue & Mask(targetType.BitWidth);
        }

        // Truncate or zero extend
        return Value & Mask(targetType.BitWidth);
    }

    private ulong Mask(BitWidth bitWidth) {
        return bitWidth switch {
            BitWidth.NIBBLE_4 => 0x0F,
            BitWidth.QUIBBLE_5 => 0b11111,
            BitWidth.BYTE_8 => 0xFF,
            BitWidth.WORD_16 => 0xFFFF,
            BitWidth.DWORD_32 => 0xFFFFFFFF,
            BitWidth.QWORD_64 => 0xFFFFFFFFFFFFFFFF,
            _ => throw new InvalidOperationException($"Unsupported bit width {bitWidth}")
        };
    }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitConstantNode(this);
    }
}