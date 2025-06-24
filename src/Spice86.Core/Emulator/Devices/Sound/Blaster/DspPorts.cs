namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Contains constants for the I/O ports used by the SoundBlaster DSP module.
/// </summary>
public static class DspPorts {
    /// <summary>
    /// The I/O port used for resetting the DSP chip.
    /// </summary>
    public const int DspReset = 0x06;

    /// <summary>
    /// The port used to write data to the DSP.
    /// </summary>
    public const int DspWriteData = 0x0C;

    /// <summary>
    /// The I/O port used for reading data from the DSP chip.
    /// </summary>
    public const int DspReadData = 0x0A;

    /// <summary>
    /// The I/O port used to get the DSP write status.
    /// </summary>
    public const int DspWriteStatus = 0x0C;

    /// <summary>
    /// The I/O port used for checking the status of the DSP chip's input buffer.
    /// </summary>
    public const int DspReadStatus = 0x0E;

    /// <summary>
    /// The I/O port used for acknowledging DMA transfers to the DSP chip.
    /// </summary>
    public const int DspDma16Acknowledge = 0x0F;

    /// <summary>
    /// The I/O port used for addressing the mixer chip.
    /// </summary>
    public const int MixerAddress = 0x04;

    /// <summary>
    /// The I/O port used for writing data to the mixer chip.
    /// </summary>
    public const int MixerData = 0x05;
}
