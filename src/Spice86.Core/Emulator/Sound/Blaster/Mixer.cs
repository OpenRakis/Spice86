namespace Spice86.Core.Emulator.Sound.Blaster;

using Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Represents the SoundBlaster mixer.
/// </summary>
public sealed class Mixer {
    private readonly SoundBlaster _blaster;

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixer"/> class with the specified SoundBlaster instance.
    /// </summary>
    /// <param name="blaster">The SoundBlaster instance to use for the mixer.</param>
    public Mixer(SoundBlaster blaster) => _blaster = blaster;

    /// <summary>
    /// Gets or sets the current mixer address.
    /// </summary>
    public int CurrentAddress { get; set; }
    
    /// <summary>
    /// Gets or sets the interrupt status register for the mixer.
    /// </summary>
    public InterruptStatus InterruptStatusRegister { get; set; }

    /// <summary>
    /// Reads data from the current mixer address.
    /// </summary>
    /// <returns>The data read from the current mixer address.</returns>
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

    /// <summary>
    /// Returns the byte value for the IRQ mixer register based on the current IRQ value of the SoundBlaster instance.
    /// </summary>
    /// <returns>The byte value for the IRQ mixer register.</returns>
    private byte GetIRQByte() {
        return _blaster.IRQ switch {
            2 => 1 << 0,
            5 => 1 << 1,
            7 => 1 << 2,
            10 => 1 << 3,
            _ => 0,
        };
    }
    
    /// <summary>
    /// Returns the byte value for the DMA mixer register based on the current DMA value of the SoundBlaster instance.
    /// </summary>
    /// <returns>The byte value for the DMA mixer register.</returns>
    private byte GetDMAByte() => (byte)(1 << _blaster.DMA);
}
