namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct ChipFloat {
    public ChipFloat() {
        Regs = new byte[(int)Reg.Count];
        Gains = new double[(int)DatasheetSpecs.TapCount];
        Taps = new ushort[(int)DatasheetSpecs.TapCount];
        Buffer = new double[(int)DatasheetSpecs.BufferLength];
        Oversampler = new OversamplerFloat[(int)OutputChannel.Count];
    }

    public byte[] Regs { get; set; }

    public double[] Gains { get; set; }

    public ushort[] Taps { get; set;  }

    public double T0d { get; set;  }

    public double[] Buffer { get; set; }

    public int Length => Buffer.Length;

    public OversamplerFloat[] Oversampler { get; set; }
}