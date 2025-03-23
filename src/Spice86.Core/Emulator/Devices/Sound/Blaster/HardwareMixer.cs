namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents the SoundBlaster hardware mixer, also known as 'CTMixer'.
/// </summary>
public class HardwareMixer {
    private readonly SoundBlasterHardwareConfig _blasterHardwareConfig;
    private readonly ILoggerService _logger;
    private readonly SoundChannel _pcmSoundChannel;
    private readonly SoundChannel _opl3fmSoundChannel;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareMixer"/> class with the specified SoundBlaster instance.
    /// </summary>
    /// <param name="soundBlasterHardwareConfig">The SoundBlaster IRQs, and DMA information.</param>
    /// <param name="opl3fmSoundChannel">The sound channel for FM synth music.</param>
    /// <param name="loggerService">The service used for logging.</param>
    /// <param name="pcmSoundChannel">The sound channel for sound effects.</param>
    public HardwareMixer(SoundBlasterHardwareConfig soundBlasterHardwareConfig, SoundChannel pcmSoundChannel, SoundChannel opl3fmSoundChannel, ILoggerService loggerService) {
        _logger = loggerService;
        _blasterHardwareConfig = soundBlasterHardwareConfig;
        _pcmSoundChannel = pcmSoundChannel;
        _opl3fmSoundChannel = opl3fmSoundChannel;
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
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _logger.Warning("Unsupported mixer register {CurrentAddress:X2}h", CurrentAddress);
                }
                return 0;
        }
    }

    /// <summary>
    /// Write data to the <see cref="CurrentAddress"/> of the hardware mixer. <br/>
    /// For example, the FM volume register is written to when the address is <c>0x26</c>.
    /// </summary>
    /// <param name="value">The value to apply.</param>
    public void Write(byte value) {
        int percentScaledValue = (int)(value / 255.0 * 100);
        switch (CurrentAddress) {
            case 0x04:  /* DAC Volume (SBPRO) */
                _pcmSoundChannel.Volume = percentScaledValue;
                break;
            case 0x26:  /* FM Volume (SBPRO) */
                _opl3fmSoundChannel.Volume = percentScaledValue;
                break;
            default:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _logger.Warning("Unsupported mixer register {CurrentAddress:X2}h", CurrentAddress);
                }
                break;
        }
    }

    /// <summary>
    /// Returns the byte value for the IRQ mixer register based on the current IRQ value of the SoundBlaster hardware.
    /// </summary>
    /// <returns>The byte value for the IRQ mixer register.</returns>
    private byte GetIRQByte() {
        return _blasterHardwareConfig.Irq switch {
            2 => 1 << 0,
            5 => 1 << 1,
            7 => 1 << 2,
            10 => 1 << 3,
            _ => 0,
        };
    }

    /// <summary>
    /// Returns the byte value for the DMA mixer register based on the current DMA value of the SoundBlaster hardware.
    /// </summary>
    /// <returns>The byte value for the DMA mixer register.</returns>
    private byte GetDMAByte() => (byte)(1 << _blasterHardwareConfig.HighDma);
}
