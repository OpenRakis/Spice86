namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// Determines aspect ratio correction factors for different video modes.
/// </summary>
public static class AspectRatioCorrectionHelper {
    /// <summary>
    /// Gets the vertical scale factor needed to correct a video mode's aspect ratio.
    /// A factor of 1.0 represents square pixels (1:1 pixel aspect ratio).
    /// A factor of 1.2 represents a 5:6 PAR (eg. Mode 13h)
    /// </summary>
    public static double GetAspectRatioCorrectionFactor(ushort width, ushort height, MemoryModel memoryModel) {
        // Text modes should maintain square pixels by default
        if (memoryModel == MemoryModel.Text) {
            return 1.0;
        }

        // Graphics modes mapped by resolution
        return (width, height) switch {
            (320, 200) => 1.2,
            (640, 200) => 2.4,
            (640, 350) => 1.0,
            (640, 480) => 1.0,
            (720, 348) => 1.0,
            (800, 600) => 1.0,
            (1024, 768) => 1.0,
            (1280, 1024) => 1.0,
            _ => 1.0
        };
    }
}
