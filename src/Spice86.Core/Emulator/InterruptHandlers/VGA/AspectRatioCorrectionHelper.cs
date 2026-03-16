namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// Determines aspect ratio correction factors for different video modes.
/// </summary>
public static class AspectRatioCorrectionHelper {
    /// <summary>
    /// Gets the vertical scale factor needed to correct a video mode's aspect ratio.
    /// A factor of 1.0 represents square pixels (1:1 pixel aspect ratio).
    /// A factor of 1.2 corrects a 5:6 pixel aspect ratio (e.g., Mode 13h requires 6:5 vertical scaling).
    /// </summary>
    /// <remarks>
    /// The correction factor is computed based on the standard 4:3 CRT aspect ratio:
    /// factor = (3 * width) / (4 * height)
    /// This formula automatically adjusts the vertical scaling to maintain the correct aspect ratio
    /// for any resolution that was designed for 4:3 displays.
    /// </remarks>
    public static double GetAspectRatioCorrectionFactor(ushort width, ushort height, MemoryModel memoryModel) {
        // Text modes should maintain square pixels by default
        if (memoryModel == MemoryModel.Text) {
            return 1.0;
        }

        // Compute aspect ratio correction factor based on 4:3 CRT aspect ratio
        return 3.0 * width / (4.0 * height);
    }
}
