namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// Pure functions for VGA aspect ratio correction. VGA Mode 13h (320x200) uses 5:6 pixel aspect ratio.
/// </summary>
public static class AspectRatioHelper {
    /// <summary>
    /// Calculates whether a scanline should be duplicated for aspect ratio correction.
    /// For VGA Mode 13h (320x200) requiring 5:6 pixel aspect ratio correction,
    /// duplicates every 5th line to achieve 1.2x vertical stretch (240 output lines).
    /// </summary>
    /// <param name="width">Display width in pixels (from current VGA mode).</param>
    /// <param name="nativeHeight">Native height in pixels (from current VGA mode).</param>
    /// <param name="currentLine">Current scanline being rendered (0-based).</param>
    /// <returns>True if this line should be duplicated.</returns>
    public static bool ShouldDuplicateLine(int width, int nativeHeight, int currentLine) {
        // VGA Mode 13h: 320x200 with 5:6 PAR needs correction to 240 lines (200 * 1.2 = 240) for 4:3 aspect
        if (width == 320 && nativeHeight == 200) {
            // Duplicate every 5th line (lines 4, 9, 14, ..., 194, 199)
            return currentLine % 5 == 4 && currentLine < nativeHeight;
        }
        return false;
    }

    /// <summary>
    /// Calculates the number of times a scanline should be drawn for aspect ratio correction.
    /// Returns baseLinesPerScanline + 1 if duplication is needed, otherwise returns baseLinesPerScanline.
    /// </summary>
    /// <param name="width">Display width in pixels.</param>
    /// <param name="nativeHeight">Native height in pixels.</param>
    /// <param name="currentLine">Current scanline being rendered (0-based).</param>
    /// <param name="baseLinesPerScanline">Base number of lines per scanline (1 or 2 for double-scan).</param>
    /// <returns>Number of times to draw this scanline.</returns>
    public static int CalculateLinesToDraw(int width, int nativeHeight, int currentLine, int baseLinesPerScanline) {
        return ShouldDuplicateLine(width, nativeHeight, currentLine) 
            ? baseLinesPerScanline + 1 
            : baseLinesPerScanline;
    }

    /// <summary>
    /// Determines whether to reset the destination address for the current draw iteration.
    /// For double-scan iterations (0, 1), reset to draw on the same line.
    /// For aspect correction duplicate (iteration 2+), don't reset to draw on new line.
    /// </summary>
    /// <param name="currentIteration">Current draw iteration (0-based).</param>
    /// <param name="baseLinesPerScanline">Base number of lines per scanline.</param>
    /// <returns>True if destination address should be reset.</returns>
    public static bool ShouldResetDestinationAddress(int currentIteration, int baseLinesPerScanline) {
        // Reset for iterations 1 onwards, but only up to baseLinesPerScanline-1
        // iteration 0: no reset (first draw)
        // iteration 1: reset (double-scan - draw on same line as iteration 0)
        // iteration 2: no reset (aspect correction - draw on NEW line)
        return currentIteration > 0 && currentIteration < baseLinesPerScanline;
    }
}
