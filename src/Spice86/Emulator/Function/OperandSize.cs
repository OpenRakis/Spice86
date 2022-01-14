namespace Spice86.Emulator.Function;
public record OperandSize {
    private OperandSize() { }

    public int Bits { get; init; } = 8;
    public OperandSizeName Name { get; init; } = OperandSizeName.Byte8;

    public static OperandSize Byte8 => new() { Bits = 8, Name = OperandSizeName.Byte8 };
    public static OperandSize Word16 => new() { Bits = 16, Name = OperandSizeName.Word16 };
    public static OperandSize Dword32 => new() { Bits = 32, Name = OperandSizeName.Dword32 };
    public static OperandSize Dword32Ptr => new() { Bits = 32, Name = OperandSizeName.Dword32Ptr };
}