namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
///     Event arguments for the video mode changed event.
/// </summary>
/// <param name="NewMode"></param>
public record VideoModeChangedEventArgs(VgaMode NewMode);