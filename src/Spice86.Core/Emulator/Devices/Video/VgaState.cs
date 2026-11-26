namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Complete state of the VGA card, as captured by <see cref="VgaCard.CaptureState" />.
/// </summary>
/// <param name="Registers">State of the registers, the DAC included.</param>
/// <param name="Memory">State of the video memory planes and latches.</param>
public sealed record VgaState(VgaRegistersState Registers, VideoMemoryState Memory);
