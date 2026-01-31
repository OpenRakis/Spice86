namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

public class ConstantNode(DataType dataType, ulong value) : ValueNode(dataType) {
    public ulong Value { get; } = value;
    
    public long SignedValue =>
        DataType.BitWidth switch {
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
        if (DataType.BitWidth < targetType.BitWidth && targetType.Signed) {
            // Sign extend
            long signedValue = SignedValue;
            return (ulong)signedValue;
        }

        // Truncate or zero extend
        return Value & Mask(targetType.BitWidth);
    }

    private ulong Mask(BitWidth bitWidth) {
        return bitWidth switch {
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