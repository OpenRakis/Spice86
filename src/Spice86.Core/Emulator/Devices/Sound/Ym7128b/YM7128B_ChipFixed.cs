namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipFixed {
    public YM7128B_ChipFixed() {
        Regs = new byte[(int)YM7128B_Reg.YM7128B_Reg_Count];
        Gains = new short[(int)YM7128B_Reg.YM7128B_Reg_T0];
        Taps = new ushort[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Buffer = new short[(int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length];
        Oversampler = new YM7128B_OversamplerFixed[(int)YM7128B_OutputChannel.YM7128B_OutputChannel_Count];
    }

    public byte[] Regs { get; set; }
    public short[] Gains { get; set; }
    public ushort[] Taps { get; set; }

    public short T0d { get; set; }

    public short Tail { get; set; }

    public short[] Buffer { get; set; }

    public int Length => Buffer.Length;

    public YM7128B_OversamplerFixed[] Oversampler { get; set; }
}
