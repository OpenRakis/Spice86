namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
internal struct ChipShort {
    public ChipShort() {
        Regs = new byte[(int)Reg.Count];
        Gains = new short[(int)DatasheetSpecs.TapCount];
        Taps = new ushort[(int)DatasheetSpecs.TapCount];
        Buffer = new short[(int)DatasheetSpecs.BufferLength];
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