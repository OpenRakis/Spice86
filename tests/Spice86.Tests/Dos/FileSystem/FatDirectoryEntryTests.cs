namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System;
using System.Text;

using Xunit;

/// <summary>
/// Unit tests for <see cref="FatDirectoryEntry"/> parsing.
/// </summary>
public class FatDirectoryEntryTests {
    [Fact]
    public void Parse_DeletedEntry_IsMarkedDeleted() {
        // Arrange
        byte[] raw = new byte[32];
        raw[0] = 0xE5; // deleted marker

        // Act
        FatDirectoryEntry entry = FatDirectoryEntry.Parse(raw.AsSpan());

        // Assert
        entry.IsDeleted.Should().BeTrue();
        entry.IsEndMarker.Should().BeFalse();
    }

    [Fact]
    public void Parse_EndMarker_IsMarkedEndMarker() {
        // Arrange
        byte[] raw = new byte[32]; // all zeroes = end marker

        // Act
        FatDirectoryEntry entry = FatDirectoryEntry.Parse(raw.AsSpan());

        // Assert
        entry.IsEndMarker.Should().BeTrue();
        entry.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Parse_RegularFile_ReadsDosNameAndSize() {
        // Arrange
        byte[] raw = new byte[32];
        Encoding.ASCII.GetBytes("README  ").AsSpan().CopyTo(raw.AsSpan(0));
        Encoding.ASCII.GetBytes("TXT").AsSpan().CopyTo(raw.AsSpan(8));
        raw[11] = 0x20; // archive attribute
        raw[26] = 5; raw[27] = 0; // first cluster = 5
        raw[28] = 42; raw[29] = 0; raw[30] = 0; raw[31] = 0; // file size = 42

        // Act
        FatDirectoryEntry entry = FatDirectoryEntry.Parse(raw.AsSpan());

        // Assert
        entry.DosName.Should().Be("README.TXT");
        entry.FirstCluster.Should().Be(5);
        entry.FileSize.Should().Be(42u);
        entry.IsDirectory.Should().BeFalse();
        entry.IsVolumeLabel.Should().BeFalse();
    }

    [Fact]
    public void Parse_DirectoryEntry_IsMarkedDirectory() {
        // Arrange
        byte[] raw = new byte[32];
        Encoding.ASCII.GetBytes("SUBDIR  ").AsSpan().CopyTo(raw.AsSpan(0));
        Encoding.ASCII.GetBytes("   ").AsSpan().CopyTo(raw.AsSpan(8));
        raw[11] = 0x10; // directory attribute

        // Act
        FatDirectoryEntry entry = FatDirectoryEntry.Parse(raw.AsSpan());

        // Assert
        entry.IsDirectory.Should().BeTrue();
        entry.DosName.Should().Be("SUBDIR");
    }

    [Fact]
    public void Parse_VolumeLabel_IsMarkedVolumeLabel() {
        // Arrange
        byte[] raw = new byte[32];
        Encoding.ASCII.GetBytes("MY DISK    ").AsSpan().CopyTo(raw.AsSpan(0));
        raw[11] = 0x08; // volume label attribute

        // Act
        FatDirectoryEntry entry = FatDirectoryEntry.Parse(raw.AsSpan());

        // Assert
        entry.IsVolumeLabel.Should().BeTrue();
        entry.IsDirectory.Should().BeFalse();
    }

    [Fact]
    public void Parse_TooShortData_ThrowsArgumentException() {
        // Arrange
        byte[] tooShort = new byte[20];

        // Act
        Action parse = () => FatDirectoryEntry.Parse(tooShort.AsSpan());

        // Assert
        parse.Should().Throw<ArgumentException>();
    }
}
