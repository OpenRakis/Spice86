namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
///     State of the Sound Blaster's DSP command interface, as captured by <see cref="SoundBlaster.CaptureState" />.
/// </summary>
/// <param name="State">Position of the DSP in its reset sequence.</param>
/// <param name="Command">Command being received, or 0 when the DSP waits for a command.</param>
/// <param name="CommandLength">Number of parameter bytes the command takes.</param>
/// <param name="InputData">The DSP's 64-byte input buffer, which holds the parameter bytes received so far.</param>
/// <param name="InputPosition">Number of parameter bytes received for the command.</param>
/// <param name="OutputData">The DSP's 64-byte circular output buffer, read through the read data port.</param>
/// <param name="OutputPosition">Index in <paramref name="OutputData" /> of the next byte to read.</param>
/// <param name="OutputUsed">Number of bytes waiting in the output buffer.</param>
/// <param name="OutputLastValue">Byte the read data port returns when the output buffer is empty.</param>
/// <param name="TestRegister">Value written by DSP command E4h and read back by command E8h.</param>
/// <param name="WriteStatusCounter">Counter of write status port reads, which reports room every eighth read.</param>
/// <param name="ResetTally">Number of DSP resets, which selects a cold or hot speaker warm-up.</param>
/// <param name="WarmupRemainingMs">Remaining speaker warm-up, during which DMA samples play as silence.</param>
public sealed record SoundBlasterDspState(
    SoundBlaster.DspState State,
    byte Command,
    byte CommandLength,
    ReadOnlyMemory<byte> InputData,
    byte InputPosition,
    ReadOnlyMemory<byte> OutputData,
    byte OutputPosition,
    byte OutputUsed,
    byte OutputLastValue,
    byte TestRegister,
    byte WriteStatusCounter,
    uint ResetTally,
    int WarmupRemainingMs);
