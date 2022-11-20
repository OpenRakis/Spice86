namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipFloat {
    public YM7128B_ChipFloat() {
        Regs_ = new byte[(int)YM7128B_Reg.YM7128B_Reg_Count];
        Gains_ = new double[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Taps_ = new ushort[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Buffer_ = new double[(int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length];
        Oversampler_ = new YM7128B_OversamplerFloat[(int)YM7128B_OutputChannel.YM7128B_OutputChannel_Count];
    }

    public byte[] Regs_ { get; set; }

    public double[] Gains_ { get; set; }

    public ushort[] Taps_ { get; set;  }

    public double T0_d_ { get; set;  }

    public double[] Buffer_ { get; set; }

    public int Length_ => Buffer_.Length;

    public YM7128B_OversamplerFloat[] Oversampler_ { get; set; }
}