namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video;
using Xunit;

public class AspectRatioHelperTest {
    [Fact]
    public void Mode13h_DuplicatesEvery5thLine() {
        // VGA Mode 13h: 320x200
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(320, 200, 4));   // Line 4
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(320, 200, 9));   // Line 9
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(320, 200, 14));  // Line 14
        Assert.True(AspectRatioHelper.ShouldDuplicateLine(320, 200, 199)); // Line 199 (last)
    }

    [Fact]
    public void Mode13h_DoesNotDuplicateOtherLines() {
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(320, 200, 0));
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(320, 200, 1));
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(320, 200, 3));
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(320, 200, 5));
    }

    [Fact]
    public void OtherModes_NeverDuplicate() {
        // 640x480
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(640, 480, 4));
        
        // 640x200
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(640, 200, 4));
        
        // 320x240
        Assert.False(AspectRatioHelper.ShouldDuplicateLine(320, 240, 4));
    }

    [Fact]
    public void Mode13h_Produces40Duplicates() {
        int duplicateCount = 0;
        for (int line = 0; line < 200; line++) {
            if (AspectRatioHelper.ShouldDuplicateLine(320, 200, line)) {
                duplicateCount++;
            }
        }
        // 200 original + 40 duplicates = 240 total (1.2x stretch)
        Assert.Equal(40, duplicateCount);
    }
}
