namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System.Text;

using Xunit;

/// <summary>
/// Unit tests for <see cref="BiosParameterBlock"/> parsing.
/// </summary>
public class BiosParameterBlockTests {
    [Fact]
    public void Parse_StandardFloppy144_ReturnsCorrectFields() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        BiosParameterBlock bpb = BiosParameterBlock.Parse(image.AsSpan(0, 512));

        // Assert
        bpb.BytesPerSector.Should().Be(512);
        bpb.SectorsPerCluster.Should().Be(1);
        bpb.ReservedSectors.Should().Be(1);
        bpb.NumberOfFats.Should().Be(2);
        bpb.RootDirEntries.Should().Be(224);
        bpb.TotalSectors16.Should().Be(2880);
        bpb.SectorsPerFat.Should().Be(9);
        bpb.MediaDescriptor.Should().Be(0xF0);
    }

    [Fact]
    public void Parse_WithExtendedBpb_ReadsVolumeLabel() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        BiosParameterBlock bpb = BiosParameterBlock.Parse(image.AsSpan(0, 512));

        // Assert
        bpb.VolumeLabel.Should().Be("TEST FLOPPY");
    }

    [Fact]
    public void Parse_TooShortSector_ThrowsInvalidDataException() {
        // Arrange
        byte[] tooShort = new byte[30];

        // Act
        System.Action parse = () => BiosParameterBlock.Parse(tooShort.AsSpan());

        // Assert
        parse.Should().Throw<System.IO.InvalidDataException>();
    }

    [Fact]
    public void Parse_ZeroBytesPerSector_ThrowsInvalidDataException() {
        // Arrange
        byte[] bad = new byte[512];
        // BytesPerSector at offset 11 is 0 (default zero-fill)

        // Act
        System.Action parse = () => BiosParameterBlock.Parse(bad.AsSpan());

        // Assert
        parse.Should().Throw<System.IO.InvalidDataException>();
    }

    [Fact]
    public void DataStartSector_Is33ForStandardFloppy() {
        // Arrange & Act
        byte[] image = new Fat12ImageBuilder().Build();
        BiosParameterBlock bpb = BiosParameterBlock.Parse(image.AsSpan(0, 512));

        // Assert: 1 reserved + 2*9 FATs + 14 root dir sectors = 33
        bpb.DataStartSector.Should().Be(33);
    }
}
