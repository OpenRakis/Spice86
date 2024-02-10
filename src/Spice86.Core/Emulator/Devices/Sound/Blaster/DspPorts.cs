namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Contains constants for the I/O ports used by the SoundBlaster DSP module.
/// </summary>
public static class DspPorts {
    /// <summary>
    /// The I/O port used for resetting the DSP chip.
    /// </summary>
    public const int DspReset = 0x226;

    /// <summary>
    /// The port used to set the DSP status.
    /// </summary>
    public const int DspWriteStatus = 0x0C;

    /// <summary>
    /// The port used to get the DSP status.
    /// </summary>
    public const int DspReadStatus = 0x0E;

    /// <summary>
    /// The I/O port used for reading data from the DSP chip.
    /// </summary>
    public const int DspReadData = 0x22A;

    /// <summary>
    /// The I/O port used for writing data to the DSP chip.
    /// </summary>
    public const int DspWrite = 0x22C;

    /// <summary>
    /// The I/O port used for checking the status of the DSP chip's input buffer.
    /// </summary>
    public const int DspReadBufferStatus = 0x22E;

    /// <summary>
    /// The I/O port used for acknowledging DMA transfers to the DSP chip.
    /// </summary>
    public const int DspDma16Acknowledge = 0x22F;

    /// <summary>
    /// The I/O port used for addressing the mixer chip.
    /// </summary>
    public const int MixerAddress = 0x224;

    /// <summary>
    /// The I/O port used for writing data to the mixer chip.
    /// </summary>
    public const int MixerData = 0x225;
}
