namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

using Spice86.Shared.Emulator.Memory;

public sealed class DataType(BitWidth bitWidth, bool signed) : IEquatable<DataType> {
    public static DataType UINT4 { get; } = new(BitWidth.NIBBLE_4, false);
    public static DataType INT4 { get; } = new(BitWidth.NIBBLE_4, true);
    public static DataType UINT5 { get; } = new(BitWidth.QUIBBLE_5, false);
    public static DataType INT5 { get; } = new(BitWidth.QUIBBLE_5, true);
    public static DataType UINT8 { get; } = new(BitWidth.BYTE_8, false);
    public static DataType INT8 { get; } = new(BitWidth.BYTE_8, true);
    public static DataType UINT16 { get; } = new(BitWidth.WORD_16, false);
    public static DataType INT16 { get; } = new(BitWidth.WORD_16, true);
    public static DataType UINT32 { get; } = new(BitWidth.DWORD_32, false);
    public static DataType INT32 { get; } = new(BitWidth.DWORD_32, true);
    public static DataType UINT64 { get; } = new(BitWidth.QWORD_64, false);
    public static DataType INT64 { get; } = new(BitWidth.QWORD_64, true);
    public static DataType BOOL { get; } = new(BitWidth.BOOL_1, false);

    public BitWidth BitWidth { get; } = bitWidth;
    public bool Signed { get; } = signed;

    public bool Equals(DataType? other) {
        if (other is null) {
            return false;
        }
        if (ReferenceEquals(this, other)) {
            return true;
        }
        return BitWidth == other.BitWidth && Signed == other.Signed;
    }

    public override bool Equals(object? obj) {
        return Equals(obj as DataType);
    }

    public override int GetHashCode() {
        return HashCode.Combine(BitWidth, Signed);
    }

    public static bool operator ==(DataType? left, DataType? right) {
        if (ReferenceEquals(left, right)) {
            return true;
        }
        if (left is null || right is null) {
            return false;
        }
        return left.Equals(right);
    }

    public static bool operator !=(DataType? left, DataType? right) => !(left == right);

    public override string ToString() {
        if (BitWidth == BitWidth.BOOL_1) {
            return "BOOL";
        }
        string sign = Signed ? "INT" : "UINT";
        return $"{sign}{(int)BitWidth}";
    }
}