namespace Spice86.Core.Emulator.Sound.Blaster;

using Spice86.Core.Emulator.Devices.Sound;

internal sealed class Mixer {
    private readonly SoundBlaster _blaster;

    public Mixer(SoundBlaster blaster) => _blaster = blaster;

    public int CurrentAddress { get; set; }
    public InterruptStatus InterruptStatusRegister { get; set; }

    public byte ReadData() {
        switch (CurrentAddress) {
            case MixerRegisters.InterruptStatus:
                return (byte)InterruptStatusRegister;

            case MixerRegisters.IRQ:
                return GetIRQByte();

            case MixerRegisters.DMA:
                return GetDMAByte();

            default:
                System.Diagnostics.Debug.WriteLine($"Unsupported mixer register {CurrentAddress:X2}h");
                return 0;
        }
    }

    private byte GetIRQByte() {
        return _blaster.IRQ switch {
            2 => 1 << 0,
            5 => 1 << 1,
            7 => 1 << 2,
            10 => 1 << 3,
            _ => 0,
        };
    }
    private byte GetDMAByte() => (byte)(1 << _blaster.DMA);
}
