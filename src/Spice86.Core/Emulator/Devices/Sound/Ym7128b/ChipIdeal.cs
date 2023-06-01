namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;

public struct ChipIdeal
{
    public ChipIdeal() {
        Regs = new byte[(int)Reg.Count];
        Gains = new double[(int)Reg.T0];
        Taps = new ushort[(int)DatasheetSpecs.TapCount];
        Buffer = Array.Empty<double>();
    }

    public byte[] Regs { get; set; }

    public double[] Gains { get; set; }

    public ushort[] Taps { get; set; }

    public double T0d { get; set; }

    public ushort Tail { get; set; }

    public double[] Buffer { get; set; }

    public int Length  => Buffer.Length;

    public ushort SampleRate { get; set; }
}