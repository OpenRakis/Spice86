namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

using Spice86.Shared.Emulator.Memory;

public class DataType(BitWidth bitWidth, bool signed) {
    public static DataType UINT8 { get; } = new(BitWidth.BYTE_8, false);
    public static DataType INT8 { get; } = new(BitWidth.BYTE_8, true);
    public static DataType UINT16 { get; } = new(BitWidth.WORD_16, false);
    public static DataType INT16 { get; } = new(BitWidth.WORD_16, true);
    public static DataType UINT32 { get; } = new(BitWidth.DWORD_32, false);
    public static DataType INT32 { get; } = new(BitWidth.DWORD_32, true);

    public BitWidth BitWidth { get; } = bitWidth;
    public bool Signed { get; } = signed;
}