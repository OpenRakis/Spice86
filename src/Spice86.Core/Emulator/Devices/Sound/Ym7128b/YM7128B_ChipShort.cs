namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipShort {
    public YM7128B_ChipShort() {
        Regs = new byte[(int)YM7128B_Reg.YM7128B_Reg_Count];
        Gains = new short[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Taps = new ushort[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Buffer = new short[(int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length];
    }

    public byte[] Regs { get; set; }

    public short[] Gains { get; set; }

    public ushort[] Taps { get; set;  }

    public short T0d { get; set;  }

    public ushort Tail { get; set; }

    public short[] Buffer { get; set; }

    public int Length => Buffer.Length;

    public ushort SampleRate { get; set; }
}