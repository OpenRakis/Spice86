namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipFloat {
    public YM7128B_ChipFloat() {
        Regs = new byte[(int)YM7128B_Reg.YM7128B_Reg_Count];
        Gains = new double[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Taps = new ushort[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Buffer = new double[(int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length];
        Oversampler = new YM7128B_OversamplerFloat[(int)YM7128B_OutputChannel.YM7128B_OutputChannel_Count];
    }

    public byte[] Regs { get; set; }

    public double[] Gains { get; set; }

    public ushort[] Taps { get; set;  }

    public double T0d { get; set;  }

    public double[] Buffer { get; set; }

    public int Length => Buffer.Length;

    public YM7128B_OversamplerFloat[] Oversampler { get; set; }
}