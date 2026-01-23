namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
///     Event arguments for the video mode changed event.
/// </summary>
public readonly record struct VideoModeChangedEventArgs {
    /// <summary>
    ///     Instantiates a new instance of <see cref="VideoModeChangedEventArgs" />.
    /// </summary>
    /// <param name="newMode">The new video mode.</param>
    public VideoModeChangedEventArgs(VgaMode newMode) {
        NewMode = newMode;
    }

    /// <summary>
    ///     The new video mode.
    /// </summary>
    public VgaMode NewMode { get; init; }

    /// <summary>
    ///     Gets the aspect ratio correction factor for the new video mode.
    ///     A value of 1.0 means square pixels (1:1 aspect ratio).
    ///     A value of 1.2 corrects a 5:6 pixel aspect ratio (for example, Mode 13h), because the vertical scaling factor is the inverse of the pixel aspect ratio's vertical component.
    /// </summary>
    public double AspectRatioCorrectionFactor => NewMode.AspectRatioCorrectionFactor;
}