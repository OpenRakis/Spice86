namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
///     Event arguments for the video mode changed event.
/// </summary>
public class VideoModeChangedEventArgs : EventArgs {
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
    public VgaMode NewMode { get; }
}