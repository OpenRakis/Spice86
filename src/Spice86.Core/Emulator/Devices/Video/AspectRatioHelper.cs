namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// Pure functions for VGA aspect ratio correction. VGA Mode 13h (320x200) uses 5:6 pixel aspect ratio.
/// </summary>
public static class AspectRatioHelper {
    /// <summary>
    /// Calculates whether a scanline should be duplicated for aspect ratio correction.
    /// For 320x200 mode, duplicates every 5th line to achieve 240 output lines (1.2x stretch).
    /// </summary>
    /// <param name="width">Display width in pixels.</param>
    /// <param name="nativeHeight">Native height in pixels.</param>
    /// <param name="currentLine">Current scanline being rendered (0-based).</param>
    /// <returns>True if this line should be duplicated.</returns>
    public static bool ShouldDuplicateLine(int width, int nativeHeight, int currentLine) {
        // VGA Mode 13h: 320x200 with 5:6 PAR needs correction to 320x240
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
    /// For aspect ratio correction, the duplicate line should NOT reset the address.
    /// </summary>
    /// <param name="currentIteration">Current draw iteration (0-based).</param>
    /// <param name="baseLinesPerScanline">Base number of lines per scanline.</param>
    /// <returns>True if destination address should be reset.</returns>
    public static bool ShouldResetDestinationAddress(int currentIteration, int baseLinesPerScanline) {
        return currentIteration < baseLinesPerScanline;
    }
}
