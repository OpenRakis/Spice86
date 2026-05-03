namespace Spice86.Tests.CdRom;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Image;

using System;
using System.Collections.Generic;

using Xunit;

/// <summary>
/// Tests for <see cref="CdRomDrive"/> multi-image disc switching.
/// </summary>
public class CdRomDriveDiscSwapTests {
    private static ICdRomImage MakeMockImage(string path) {
        ICdRomImage mock = Substitute.For<ICdRomImage>();
        mock.ImagePath.Returns(path);
        mock.Tracks.Returns(new List<CdTrack>());
        mock.TotalSectors.Returns(300);
        return mock;
    }

    [Fact]
    public void SingleImageDrive_ImageCount_IsOne() {
        // Arrange / Act
        CdRomDrive drive = new CdRomDrive(MakeMockImage("disc1.iso"));

        // Assert
        drive.ImageCount.Should().Be(1);
    }

    [Fact]
    public void AddImage_IncreasesImageCount() {
        // Arrange
        CdRomDrive drive = new CdRomDrive(MakeMockImage("disc1.iso"));

        // Act
        drive.AddImage(MakeMockImage("disc2.iso"));

        // Assert
        drive.ImageCount.Should().Be(2);
    }

    [Fact]
    public void SwapToNextDisc_WithTwoImages_SwitchesToSecond() {
        // Arrange
        ICdRomImage disc1 = MakeMockImage("disc1.iso");
        ICdRomImage disc2 = MakeMockImage("disc2.iso");
        CdRomDrive drive = new CdRomDrive(disc1);
        drive.AddImage(disc2);

        // Act
        drive.SwapToNextDisc();

        // Assert
        drive.Image.ImagePath.Should().Be("disc2.iso");
    }

    [Fact]
    public void SwapToNextDisc_AfterLastImage_WrapsToFirst() {
        // Arrange
        ICdRomImage disc1 = MakeMockImage("disc1.iso");
        ICdRomImage disc2 = MakeMockImage("disc2.iso");
        CdRomDrive drive = new CdRomDrive(disc1);
        drive.AddImage(disc2);

        // Act — swap twice to cycle back
        drive.SwapToNextDisc();
        drive.SwapToNextDisc();

        // Assert
        drive.Image.ImagePath.Should().Be("disc1.iso");
    }

    [Fact]
    public void SwapToNextDisc_SingleImageDrive_NoEffect() {
        // Arrange
        ICdRomImage disc1 = MakeMockImage("disc1.iso");
        CdRomDrive drive = new CdRomDrive(disc1);

        // Act
        drive.SwapToNextDisc();

        // Assert — still the same image
        drive.Image.ImagePath.Should().Be("disc1.iso");
    }

    [Fact]
    public void SwapToNextDisc_NotifiesMediaChanged() {
        // Arrange
        ICdRomImage disc1 = MakeMockImage("disc1.iso");
        ICdRomImage disc2 = MakeMockImage("disc2.iso");
        CdRomDrive drive = new CdRomDrive(disc1);
        drive.AddImage(disc2);
        drive.MediaState.ReadAndClearMediaChanged(); // clear initial flag

        // Act
        drive.SwapToNextDisc();

        // Assert — media-changed flag is set after swap
        drive.MediaState.ReadAndClearMediaChanged().Should().BeTrue();
    }

    [Fact]
    public void MultiImageConstructor_SetsFirstImageActive() {
        // Arrange
        ICdRomImage disc1 = MakeMockImage("disc1.iso");
        ICdRomImage disc2 = MakeMockImage("disc2.iso");

        // Act
        CdRomDrive drive = new CdRomDrive(new List<ICdRomImage> { disc1, disc2 });

        // Assert
        drive.Image.ImagePath.Should().Be("disc1.iso");
        drive.ImageCount.Should().Be(2);
    }

    [Fact]
    public void MultiImageConstructor_EmptyList_ThrowsArgumentException() {
        // Act
        Action act = () => _ = new CdRomDrive(new List<ICdRomImage>());

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
