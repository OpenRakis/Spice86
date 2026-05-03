namespace Spice86.Tests.Dos.Floppy;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// Tests for FloppyDiskDrive.SwapToIndex and AllImagePaths.
/// </summary>
public class FloppyDiskDriveSwapTests {
    private static byte[] CreateMinimalFloppyImage() {
        byte[] data = new byte[1474560];
        // BPB: bytes per sector = 512, sectors per track = 18, heads = 2, total sectors = 2880
        data[0x0B] = 0x00;
        data[0x0C] = 0x02; // 512 bytes per sector
        data[0x0D] = 0x01; // 1 sector per cluster
        data[0x0E] = 0x01;
        data[0x0F] = 0x00; // 1 reserved sector
        data[0x10] = 0x02; // 2 FATs
        data[0x11] = 0xE0;
        data[0x12] = 0x00; // 224 root dir entries
        data[0x13] = 0x40;
        data[0x14] = 0x0B; // 2880 total sectors
        data[0x15] = 0xF0; // media descriptor
        data[0x16] = 0x09;
        data[0x17] = 0x00; // 9 sectors per FAT
        data[0x18] = 0x12;
        data[0x19] = 0x00; // 18 sectors per track
        data[0x1A] = 0x02;
        data[0x1B] = 0x00; // 2 heads
        // Volume label in boot sector
        byte[] label = System.Text.Encoding.ASCII.GetBytes("TEST       ");
        label.CopyTo(data, 0x2B);
        return data;
    }

    /// <summary>
    /// SwapToIndex with a valid index should switch to the correct image.
    /// </summary>
    [Fact]
    public void SwapToIndex_ValidIndex_SwitchesToImage() {
        // Arrange
        byte[] imgData0 = CreateMinimalFloppyImage();
        byte[] imgData1 = CreateMinimalFloppyImage();
        FloppyDiskDrive drive = new FloppyDiskDrive { DriveLetter = 'A' };
        drive.MountImage(imgData0, "/images/disk1.img");
        drive.AddImage(imgData1, "/images/disk2.img");

        // Act
        drive.SwapToIndex(1);

        // Assert
        drive.ImagePath.Should().Be("/images/disk2.img");
    }

    /// <summary>
    /// SwapToIndex with an out-of-range index should have no effect.
    /// </summary>
    [Fact]
    public void SwapToIndex_OutOfRange_HasNoEffect() {
        // Arrange
        byte[] imgData = CreateMinimalFloppyImage();
        FloppyDiskDrive drive = new FloppyDiskDrive { DriveLetter = 'A' };
        drive.MountImage(imgData, "/images/disk1.img");

        // Act
        drive.SwapToIndex(5);

        // Assert
        drive.ImagePath.Should().Be("/images/disk1.img", "out-of-range index should not change the image");
    }

    /// <summary>
    /// AllImagePaths should return all registered image paths in order.
    /// </summary>
    [Fact]
    public void AllImagePaths_ReturnsAllPathsInOrder() {
        // Arrange
        byte[] imgData0 = CreateMinimalFloppyImage();
        byte[] imgData1 = CreateMinimalFloppyImage();
        FloppyDiskDrive drive = new FloppyDiskDrive { DriveLetter = 'A' };
        drive.MountImage(imgData0, "/images/disk1.img");
        drive.AddImage(imgData1, "/images/disk2.img");

        // Act
        IReadOnlyList<string> paths = drive.AllImagePaths;

        // Assert
        paths.Should().HaveCount(2);
        paths[0].Should().Be("/images/disk1.img");
        paths[1].Should().Be("/images/disk2.img");
    }
}
