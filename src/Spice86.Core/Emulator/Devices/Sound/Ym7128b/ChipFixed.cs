namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
internal struct ChipFixed {
    public ChipFixed() {
        Regs = new byte[(int)Reg.Count];
        Gains = new short[(int)Reg.T0];
        Taps = new ushort[(int)DatasheetSpecs.TapCount];
        Buffer = new short[(int)DatasheetSpecs.BufferLength];
        Oversampler = new OversamplerFixed[(int)OutputChannel.Count];
    }

    public byte[] Regs { get; set; }
    public short[] Gains { get; set; }
    public ushort[] Taps { get; set; }

    public short T0d { get; set; }

    public short Tail { get; set; }

    public short[] Buffer { get; set; }

    public int Length => Buffer.Length;

    public OversamplerFixed[] Oversampler { get; set; }
}
