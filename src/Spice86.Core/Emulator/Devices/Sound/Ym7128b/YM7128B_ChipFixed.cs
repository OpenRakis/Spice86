namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipFixed {
    public YM7128B_ChipFixed() {
        Regs_ = new byte[(int)YM7128B_Reg.YM7128B_Reg_Count];
        Gains_ = new short[(int)YM7128B_Reg.YM7128B_Reg_T0];
        Taps_ = new ushort[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Buffer_ = new short[(int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length];
        Oversampler_ = new YM7128B_OversamplerFixed[(int)YM7128B_OutputChannel.YM7128B_OutputChannel_Count];
    }

    public byte[] Regs_ { get; set; }
    public short[] Gains_ { get; set; }
    public ushort[] Taps_ { get; set; }

    public short T0_d_ { get; set; }

    public short Tail_ { get; set; }

    public short[] Buffer_ { get; set; }

    public YM7128B_OversamplerFixed[] Oversampler_ { get; set; }
}
