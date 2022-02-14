namespace Spice86.Emulator.Function;
public record OperandSize {
    public static readonly OperandSize Byte8 = new() { Bits = 8, Name = OperandSizeName.Byte8 };
    public static readonly OperandSize Word16 = new() { Bits = 16, Name = OperandSizeName.Word16 };
    public static readonly OperandSize Dword32 = new() { Bits = 32, Name = OperandSizeName.Dword32 };
    public static readonly OperandSize Dword32Ptr = new() { Bits = 32, Name = OperandSizeName.Dword32Ptr };

    private OperandSize() { }

    public int Bits { get; init; }
    public OperandSizeName Name { get; init; }
}