namespace Spice86.Tests.Dos.CdRom;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Image;

using Xunit;

/// <summary>
/// Tests for CdRomDrive.SwapToIndex and AllImagePaths.
/// </summary>
public class CdRomDriveSwapTests {
    /// <summary>
    /// SwapToIndex with a valid index should switch to the correct image.
    /// </summary>
    [Fact]
    public void SwapToIndex_ValidIndex_SwitchesToCorrectImage() {
        // Arrange
        ICdRomImage image0 = Substitute.For<ICdRomImage>();
        ICdRomImage image1 = Substitute.For<ICdRomImage>();
        image0.ImagePath.Returns("/images/disc1.iso");
        image1.ImagePath.Returns("/images/disc2.iso");
        image0.TotalSectors.Returns(100);
        image1.TotalSectors.Returns(200);
        image0.Tracks.Returns(new List<CdTrack>());
        image1.Tracks.Returns(new List<CdTrack>());
        CdRomDrive drive = new CdRomDrive(new List<ICdRomImage> { image0, image1 });

        // Act
        drive.SwapToIndex(1);

        // Assert
        drive.Image.Should().BeSameAs(image1);
    }

    /// <summary>
    /// SwapToIndex with an out-of-range index should have no effect.
    /// </summary>
    [Fact]
    public void SwapToIndex_OutOfRange_HasNoEffect() {
        // Arrange
        ICdRomImage image0 = Substitute.For<ICdRomImage>();
        ICdRomImage image1 = Substitute.For<ICdRomImage>();
        image0.ImagePath.Returns("/images/disc1.iso");
        image1.ImagePath.Returns("/images/disc2.iso");
        image0.TotalSectors.Returns(100);
        image1.TotalSectors.Returns(200);
        image0.Tracks.Returns(new List<CdTrack>());
        image1.Tracks.Returns(new List<CdTrack>());
        CdRomDrive drive = new CdRomDrive(new List<ICdRomImage> { image0, image1 });

        // Act
        drive.SwapToIndex(5);

        // Assert
        drive.Image.Should().BeSameAs(image0, "out-of-range index should leave current image unchanged");
    }

    /// <summary>
    /// SwapToIndex with a negative index should have no effect.
    /// </summary>
    [Fact]
    public void SwapToIndex_NegativeIndex_HasNoEffect() {
        // Arrange
        ICdRomImage image0 = Substitute.For<ICdRomImage>();
        image0.ImagePath.Returns("/images/disc1.iso");
        image0.TotalSectors.Returns(100);
        image0.Tracks.Returns(new List<CdTrack>());
        CdRomDrive drive = new CdRomDrive(image0);

        // Act
        drive.SwapToIndex(-1);

        // Assert
        drive.Image.Should().BeSameAs(image0, "negative index should leave current image unchanged");
    }

    /// <summary>
    /// AllImagePaths should return the paths of all registered images in order.
    /// </summary>
    [Fact]
    public void AllImagePaths_ReturnsAllPathsInOrder() {
        // Arrange
        ICdRomImage image0 = Substitute.For<ICdRomImage>();
        ICdRomImage image1 = Substitute.For<ICdRomImage>();
        image0.ImagePath.Returns("/images/disc1.iso");
        image1.ImagePath.Returns("/images/disc2.iso");
        image0.Tracks.Returns(new List<CdTrack>());
        image1.Tracks.Returns(new List<CdTrack>());
        CdRomDrive drive = new CdRomDrive(new List<ICdRomImage> { image0, image1 });

        // Act
        IReadOnlyList<string> paths = drive.AllImagePaths;

        // Assert
        paths.Should().HaveCount(2);
        paths[0].Should().Be("/images/disc1.iso");
        paths[1].Should().Be("/images/disc2.iso");
    }
}
