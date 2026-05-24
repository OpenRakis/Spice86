namespace Spice86.Tests.Dos;

using System;
using System.IO;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Tests.Utility;

using Xunit;

public class CdRomParityTests {
    [Fact]
    public void VirtualIsoImage_Read_RawModeOnCookedTrack_ReturnsZero() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        using VirtualIsoImage image = CreateVirtualIsoImage(tempFile);
        byte[] buffer = new byte[2352];

        // Act
        int bytesRead = image.Read(16, buffer, CdSectorMode.Raw2352);

        // Assert
        bytesRead.Should().Be(0,
            "DOSBox rejects raw 2352-byte reads from cooked-only virtual ISO tracks");
    }

    [Fact]
    public void IsoImage_Read_RawModeOnCookedTrack_ReturnsZero() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        string isoPath = CreateIsoFile(tempFile);
        using IsoImage image = new(isoPath);
        byte[] buffer = new byte[2352];

        // Act
        int bytesRead = image.Read(16, buffer, CdSectorMode.Raw2352);

        // Assert
        bytesRead.Should().Be(0,
            "DOSBox rejects raw 2352-byte reads from plain ISO images because they only expose cooked 2048-byte sectors");
    }

    [Fact]
    public void CdRomDrive_Eject_DoesNotOpenDoorOrFlagMediaChangedForMountedImages() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        using VirtualIsoImage image = CreateVirtualIsoImage(tempFile);
        CdRomDrive drive = new(image);
        bool initialMediaChanged = drive.MediaState.ReadAndClearMediaChanged();

        // Act
        drive.Eject();
        bool postEjectMediaChanged = drive.MediaState.ReadAndClearMediaChanged();

        // Assert
        initialMediaChanged.Should().BeTrue(
            "newly mounted images should expose the initial media-changed notification before the eject check runs");
        drive.MediaState.IsDoorOpen.Should().BeFalse(
            "DOSBox image-backed drives do not transition to an open tray state on eject requests");
        postEjectMediaChanged.Should().BeFalse(
            "DOSBox image-backed drives treat eject as a no-op rather than a media change");
    }

    private static VirtualIsoImage CreateVirtualIsoImage(TempFile tempFile) {
        string sourceDirectory = tempFile.CreateDirectory("source");
        File.WriteAllText(Path.Combine(sourceDirectory, "README.TXT"), "Spice86");
        return new VirtualIsoImage(sourceDirectory, "SPICE86");
    }

    private static string CreateIsoFile(TempFile tempFile) {
        using VirtualIsoImage virtualIsoImage = CreateVirtualIsoImage(tempFile);
        byte[] isoBytes = new byte[virtualIsoImage.TotalSectors * 2048];
        for (int lba = 0; lba < virtualIsoImage.TotalSectors; lba++) {
            int offset = lba * 2048;
            int bytesRead = virtualIsoImage.Read(lba, isoBytes.AsSpan(offset, 2048), CdSectorMode.CookedData2048);
            if (bytesRead != 2048) {
                throw new InvalidOperationException($"Expected to materialize a full cooked sector at LBA {lba}.");
            }
        }

        return tempFile.CreateFile("disc.iso", isoBytes);
    }
}