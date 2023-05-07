namespace Spice86.Core.Emulator.Sound.Blaster;

/// <summary>
/// Contains constants representing various Sound Blaster DSP commands.
/// </summary>
public static class Commands {
    /// <summary>
    /// Command to output data directly to the Sound Blaster's DAC in 8-bit mode.
    /// </summary>
    public const byte DirectModeOutput = 0x10;

    /// <summary>
    /// Command to read data from the Sound Blaster's input port in 8-bit mode.
    /// </summary>
    public const byte DirectModeInput = 0x20;

    /// <summary>
    /// Command to set the time constant for the Sound Blaster's auto-init DMA mode.
    /// </summary>
    public const byte SetTimeConstant = 0x40;

    /// <summary>
    /// Command to output data using single-cycle DMA mode in 8-bit mode.
    /// </summary>
    public const byte SingleCycleDmaOutput8 = 0x14;

    /// <summary>
    /// Command to pause DMA mode.
    /// </summary>
    public const byte PauseDmaMode = 0xD0;

    /// <summary>
    /// Command to continue DMA mode.
    /// </summary>
    public const byte ContinueDmaMode = 0xD4;

    /// <summary>
    /// Command to get the Sound Blaster's identification byte.
    /// </summary>
    public const byte DspIdentification = 0xE0;

    /// <summary>
    /// Command to get the Sound Blaster's version number.
    /// </summary>
    public const byte GetVersionNumber = 0xE1;

    /// <summary>
    /// Command to output data using auto-init DMA mode in 8-bit mode.
    /// </summary>
    public const byte AutoInitDmaOutput8 = 0x1C;

    /// <summary>
    /// Command to exit auto-init DMA mode in 8-bit mode.
    /// </summary>
    public const byte ExitAutoInit8 = 0xDA;

    /// <summary>
    /// Command to output data using high-speed auto-init DMA mode in 8-bit mode.
    /// </summary>
    public const byte HighSpeedAutoInitDmaOutput8 = 0x90;

    /// <summary>
    /// Command to output data using high-speed single-cycle DMA mode in 8-bit mode.
    /// </summary>
    public const byte HighSpeedSingleCycleDmaOutput8 = 0x91;

    /// <summary>
    /// Command to turn on the Sound Blaster's speaker.
    /// </summary>
    public const byte TurnOnSpeaker = 0xD1;

    /// <summary>
    /// Command to turn off the Sound Blaster's speaker.
    /// </summary>
    public const byte TurnOffSpeaker = 0xD3;

    /// <summary>
    /// Command to set the block transfer size for the Sound Blaster's auto-init DMA mode.
    /// </summary>
    public const byte SetBlockTransferSize = 0x48;

    /// <summary>
    /// Command to raise the Sound Blaster's IRQ.
    /// </summary>
    public const byte RaiseIrq8 = 0xF2;

    /// <summary>
    /// Command to set the Sound Blaster's sample rate in 8-bit mode.
    /// </summary>
    public const byte SetSampleRate = 0x41;

    /// <summary>
    /// Command to set the Sound Blaster's input sample rate in 8-bit mode.
    /// </summary>
    public const byte SetInputSampleRate = 0x42;

    /// <summary>
    /// Single-cycle DMA output with 16-bit data.
    /// </summary>
    public const byte SingleCycleDmaOutput16 = 0xB0;

    /// <summary>
    /// Single-cycle DMA output with 16-bit data and FIFO mode.
    /// </summary>
    public const byte SingleCycleDmaOutput16Fifo = 0xB2;

    /// <summary>
    /// Auto-init DMA output with 16-bit data.
    /// </summary>
    public const byte AutoInitDmaOutput16 = 0xB4;

    /// <summary>
    /// Auto-init DMA output with 16-bit data and FIFO mode.
    /// </summary>
    public const byte AutoInitDmaOutput16Fifo = 0xB6;

    /// <summary>
    /// Single-cycle DMA output with 8-bit data and alternate format.
    /// </summary>
    public const byte SingleCycleDmaOutput8_Alt = 0xC0;

    /// <summary>
    /// Single-cycle DMA output with 8-bit data, FIFO mode, and alternate format.
    /// </summary>
    public const byte SingleCycleDmaOutput8Fifo_Alt = 0xC2;

    /// <summary>
    /// Auto-init DMA output with 8-bit data and alternate format.
    /// </summary>
    public const byte AutoInitDmaOutput8_Alt = 0xC4;

    /// <summary>
    /// Auto-init DMA output with 8-bit data, FIFO mode, and alternate format.
    /// </summary>
    public const byte AutoInitDmaOutput8Fifo_Alt = 0xC6;

    /// <summary>
    /// Pause DMA mode for 16-bit data.
    /// </summary>
    public const byte PauseDmaMode16 = 0xD5;

    /// <summary>
    /// Continue DMA mode for 16-bit data.
    /// </summary>
    public const byte ContinueDmaMode16 = 0xD6;

    /// <summary>
    /// Exit DMA mode for 16-bit data.
    /// </summary>
    public const byte ExitDmaMode16 = 0xD9;

    /// <summary>
    /// Pause for a specified duration.
    /// </summary>
    public const byte PauseForDuration = 0x80;

    /// <summary>
    /// Single-cycle DMA output with ADPCM4 reference.
    /// </summary>
    public const byte SingleCycleDmaOutputADPCM4Ref = 0x75;

    /// <summary>
    /// Single-cycle DMA output with ADPCM4 data.
    /// </summary>
    public const byte SingleCycleDmaOutputADPCM4 = 0x7D;

    /// <summary>
    /// Single-cycle DMA output with ADPCM3 reference.
    /// </summary>
    public const byte SingleCycleDmaOutputADPCM3Ref = 0x77;

    /// <summary>
    /// Single-cycle DMA output with ADPCM3 data.
    /// </summary>
    public const byte SingleCycleDmaOutputADPCM3 = 0x76;

    /// <summary>
    /// Single-cycle DMA output with ADPCM2 reference.
    /// </summary>
    public const byte SingleCycleDmaOutputADPCM2Ref = 0x17;

    /// <summary>
    /// Single-cycle DMA output with ADPCM2 data.
    /// </summary>
    public const byte SingleCycleDmaOutputADPCM2 = 0x16;
}
