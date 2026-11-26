namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
///     State of the Sound Blaster's DMA transfer, as captured by <see cref="SoundBlaster.CaptureState" />.
/// </summary>
/// <remarks>
///     The DMA channel's own registers are part of the DMA controllers' state.
/// </remarks>
/// <param name="Mode">Sample format of the transfer.</param>
/// <param name="ChannelNumber">
///     Number of the DMA channel the transfer reads, or <see langword="null" /> when no transfer was set up.
/// </param>
/// <param name="Stereo">Whether the samples are stereo.</param>
/// <param name="Signed">Whether the samples are signed.</param>
/// <param name="AutoInit">Whether the transfer restarts with <paramref name="AutoSize" /> bytes when it ends.</param>
/// <param name="FirstTransfer">Whether no block was played since the last DSP reset.</param>
/// <param name="Rate">Bytes read per second.</param>
/// <param name="Multiplier">Bytes per sample frame, in fixed point with 14 fractional bits.</param>
/// <param name="SingleSize">Length of the next single-cycle block, in bytes.</param>
/// <param name="AutoSize">Length of an auto-init block, in bytes.</param>
/// <param name="Left">Bytes left in the current block.</param>
/// <param name="Min">Smallest number of bytes read at a time, three milliseconds' worth.</param>
/// <param name="RemainSize">1 when a stereo sample waits for its other half, otherwise 0.</param>
/// <param name="PendingSample8Bit">The 8-bit sample that waits for its other half.</param>
/// <param name="PendingSample16Bit">The 16-bit sample that waits for its other half.</param>
public sealed record SoundBlasterDmaTransferState(
    SoundBlaster.DmaMode Mode,
    byte? ChannelNumber,
    bool Stereo,
    bool Signed,
    bool AutoInit,
    bool FirstTransfer,
    uint Rate,
    uint Multiplier,
    uint SingleSize,
    uint AutoSize,
    uint Left,
    uint Min,
    uint RemainSize,
    byte PendingSample8Bit,
    short PendingSample16Bit);
