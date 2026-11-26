namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Complete state of the VGA video memory, as captured by <see cref="VideoMemory.CaptureState" />.
/// </summary>
/// <param name="VRam">The four planes of 64 KiB, interleaved as <see cref="VideoMemory.VRam" /> stores them.</param>
/// <param name="Latches">The four plane latches, loaded by the last read of video memory.</param>
public sealed record VideoMemoryState(ReadOnlyMemory<byte> VRam, ReadOnlyMemory<byte> Latches);
