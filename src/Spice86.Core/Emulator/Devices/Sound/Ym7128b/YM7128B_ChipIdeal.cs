namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;

public struct YM7128B_ChipIdeal
{
    public YM7128B_ChipIdeal() {
        Regs = new byte[(int)YM7128B_Reg.YM7128B_Reg_Count];
        Gains = new double[(int)YM7128B_Reg.YM7128B_Reg_T0];
        Taps = new ushort[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
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