namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video;
using Xunit;

/// <summary>
/// Tests for AspectRatioHelper pure functions that determine VGA scanline duplication for aspect ratio correction.
/// </summary>
public class AspectRatioHelperTest {
    /// <summary>
    /// Verifies that VGA Mode 13h (320x200) correctly identifies every 5th scanline for duplication.
    /// Lines 4, 9, 14, ..., 199 should be marked for duplication to achieve 1.2x vertical stretch.
    /// </summary>
    [Fact]
    public void Mode13h_DuplicatesEvery5thLine() {
        // Arrange: VGA Mode 13h parameters
        bool needsAspectCorrection = true; // Mode 13h requires aspect correction
        int nativeHeight = 200;
        
        // Act & Assert: Check specific 5th lines should duplicate
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 4));
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 9));
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 14));
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 199));
    }

    /// <summary>
    /// Verifies that VGA Mode 13h does not duplicate non-5th scanlines.
    /// Only every 5th line (4, 9, 14...) should be duplicated, others should not.
    /// </summary>
    [Fact]
    public void Mode13h_DoesNotDuplicateOtherLines() {
        // Arrange: VGA Mode 13h parameters
        bool needsAspectCorrection = true; // Mode 13h requires aspect correction
        int nativeHeight = 200;
        
        // Act & Assert: Non-5th lines should not duplicate
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 0));
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 1));
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 3));
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, 5));
    }

    /// <summary>
    /// Verifies that modes not requiring aspect correction do not trigger scanline duplication.
    /// When needsAspectCorrection is false, no lines should be duplicated.
    /// </summary>
    [Fact]
    public void OtherModes_NeverDuplicate() {
        // Arrange & Act & Assert: Modes that don't need aspect correction
        bool needsAspectCorrection = false;
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, 480, 4));  // VGA 640x480
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, 200, 4));  // Other 200-line mode not needing correction
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, 240, 4));  // Already corrected
    }

    /// <summary>
    /// Verifies that VGA Mode 13h produces exactly 40 duplicate scanlines.
    /// This achieves 200 original + 40 duplicates = 240 total lines (1.2x stretch for 4:3 aspect).
    /// </summary>
    [Fact]
    public void Mode13h_Produces40Duplicates() {
        // Arrange: VGA Mode 13h parameters
        bool needsAspectCorrection = true; // Mode 13h requires aspect correction
        int nativeHeight = 200;
        
        // Act: Count duplicates across all scanlines
        int duplicateCount = 0;
        for (int line = 0; line < nativeHeight; line++) {
            if (AspectRatioHelper.ShouldDuplicateLine(needsAspectCorrection, nativeHeight, line)) {
                duplicateCount++;
            }
        }
        
        // Assert: Should produce exactly 40 duplicates (200 × 6/5 = 240)
        Assert.Equal(40, duplicateCount);
    }

    /// <summary>
    /// Verifies that CalculateLinesToDraw adds one extra line for scanlines needing duplication.
    /// For Mode 13h, lines 4, 9, 14... should draw baseLinesPerScanline + 1 times.
    /// </summary>
    [Fact]
    public void CalculateLinesToDraw_AddsOneForDuplicates() {
        // Arrange
        bool needsAspectCorrection = true; // Mode 13h requires aspect correction
        int nativeHeight = 200;
        int baseLinesPerScanline = 1;
        
        // Act & Assert: Line 4 should draw 2 times (base 1 + duplicate 1)
        Assert.Equal(2, AspectRatioHelper.CalculateLinesToDraw(needsAspectCorrection, nativeHeight, 4, baseLinesPerScanline));
        
        // Act & Assert: Line 0 should draw 1 time (base only)
        Assert.Equal(1, AspectRatioHelper.CalculateLinesToDraw(needsAspectCorrection, nativeHeight, 0, baseLinesPerScanline));
    }

    /// <summary>
    /// Verifies that ShouldResetDestinationAddress correctly identifies when to reset the destination address.
    /// For iteration 0: no reset needed (first draw uses latched address).
    /// For iteration 1 in double-scan mode: reset to draw on same line.
    /// For iteration 2+ (aspect correction): no reset to draw on new lines.
    /// </summary>
    [Fact]
    public void ShouldResetDestinationAddress_CorrectlyIdentifiesBaseAndDuplicate() {
        // Arrange
        int baseLinesPerScanline = 2; // Double-scan mode
        
        // Act & Assert: Iteration 0 should NOT reset (first draw uses latched address)
        Assert.False(AspectRatioHelper.ShouldResetDestinationAddress(0, baseLinesPerScanline));
        
        // Act & Assert: Iteration 1 should reset (double-scan - draw on same line)
        Assert.True(AspectRatioHelper.ShouldResetDestinationAddress(1, baseLinesPerScanline));
        
        // Act & Assert: Iteration 2 should NOT reset (aspect correction - draw on new line)
        Assert.False(AspectRatioHelper.ShouldResetDestinationAddress(2, baseLinesPerScanline));
    }
}
