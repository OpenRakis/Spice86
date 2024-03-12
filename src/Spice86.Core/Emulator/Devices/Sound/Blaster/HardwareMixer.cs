namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents the SoundBlaster hardware mixer, also known as 'CTMixer'.
/// </summary>
public sealed class HardwareMixer {
    private readonly SoundBlaster _blaster;
    private readonly ILoggerService _logger;
    private readonly SoundChannel _pcmSoundChannel;
    private readonly SoundChannel _opl3fmSoundChannel;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareMixer"/> class with the specified SoundBlaster instance.
    /// </summary>
    /// <param name="blaster">The SoundBlaster instance to use for the mixer.</param>
    /// <param name="loggerService">The service used for logging.</param>
    public HardwareMixer(SoundBlaster blaster, ILoggerService loggerService) {
        _blaster = blaster;
        _logger = loggerService;
        _pcmSoundChannel = blaster.PCMSoundChannel;
        _opl3fmSoundChannel = blaster.FMSynthSoundChannel;
    }

    /// <summary>
    /// Gets or sets the current mixer register in use.
    /// </summary>
    public int CurrentAddress { get; set; }

    /// <summary>
    /// Gets or sets the interrupt status register for the mixer.
    /// </summary>
    public InterruptStatus InterruptStatusRegister { get; set; }

    /// <summary>
    /// Reads data from the <see cref="CurrentAddress"/>
    /// </summary>
    /// <returns>The data read from the current in use mixer register.</returns>
    public byte ReadData() {
        switch (CurrentAddress) {
            case MixerRegisters.InterruptStatus:
                return (byte)InterruptStatusRegister;

            case MixerRegisters.IRQ:
                return GetIRQByte();

            case MixerRegisters.DMA:
                return GetDMAByte();

            default:
                _logger.Warning("Unsupported mixer register {CurrentAddress:X2}h", CurrentAddress);
                return 0;
        }
    }

    /// <summary>
    /// Write data to the <see cref="CurrentAddress"/> of the hardware mixer. <br/>
    /// For example, the FM volume register is written to when the address is 0x26.
    /// </summary>
    /// <param name="value">The value to apply.</param>
    public void Write(byte value) {
        int scaledValue = (int)(value / 255.0 * 100);
        switch (CurrentAddress) {
            case 0x04:  /* DAC Volume (SBPRO) */
                _pcmSoundChannel.Volume = scaledValue;
                break;
            case 0x26:  /* FM Volume (SBPRO) */
                _opl3fmSoundChannel.Volume = scaledValue;
                break;
            default:
                _logger.Warning("Unsupported mixer register {CurrentAddress:X2}h", CurrentAddress);
                break;
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
