namespace Spice86.Storage.Tests.FileSystem.BootSector;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.BootSector;

using Xunit;

/// <summary>
/// TDD specifications for <see cref="MutableBiosParameterBlock"/>, <see cref="FatBootSectorCodec"/>
/// and <see cref="FatBootSectorValidator"/>. Covers Phase 1a boot sector mutation behaviour.
/// </summary>
public sealed class FatBootSectorTests {
    [Fact]
    public void MutableBpb_Serialize_ProducesExactByteLayout_Fat12() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat12FloppyBpb();
        byte[] buffer = new byte[512];

        // Act
        bpb.Serialize(buffer, FatType.Fat12);

        // Assert - spot check key offsets per FAT specification.
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(11, 2)).Should().Be(512);
        buffer[13].Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(14, 2)).Should().Be(1);
        buffer[16].Should().Be(2);
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(17, 2)).Should().Be(224);
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(19, 2)).Should().Be(2880);
        buffer[21].Should().Be(0xF0);
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(22, 2)).Should().Be(9);
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(24, 2)).Should().Be(18);
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(26, 2)).Should().Be(2);
        buffer[38].Should().Be(0x29, "extended boot signature is at offset 38 on FAT12/16");
        Encoding.ASCII.GetString(buffer.AsSpan(54, 8)).Should().Be("FAT12   ");
    }

    [Fact]
    public void MutableBpb_Serialize_WritesFat32ExtendedFields() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat32Bpb();
        byte[] buffer = new byte[512];

        // Act
        bpb.Serialize(buffer, FatType.Fat32);

        // Assert.
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(36, 4)).Should().Be(1024, "sectors per FAT32");
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(44, 4)).Should().Be(2, "root cluster");
        buffer[66].Should().Be(0x29, "extended boot signature is at offset 66 on FAT32");
        Encoding.ASCII.GetString(buffer.AsSpan(82, 8)).Should().Be("FAT32   ");
    }

    [Fact]
    public void MutableBpb_Serialize_ThrowsWhenBufferTooShort() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat12FloppyBpb();
        byte[] buffer = new byte[40];

        // Act
        Action act = () => bpb.Serialize(buffer, FatType.Fat12);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MutableBpb_Clone_IsIndependentDeepCopy() {
        // Arrange
        MutableBiosParameterBlock original = NewFat12FloppyBpb();
        MutableBiosParameterBlock clone = original.Clone();

        // Act
        clone.SectorsPerCluster = 4;
        clone.VolumeLabel = "OTHER LABEL";

        // Assert
        original.SectorsPerCluster.Should().Be(1);
        original.VolumeLabel.Should().NotBe("OTHER LABEL");
    }

    [Fact]
    public void Codec_Parse_Fat12_ExtractsBpbCorrectly() {
        // Arrange
        byte[] sector = BuildBootSector(NewFat12FloppyBpb(), FatType.Fat12);

        // Act
        MutableBiosParameterBlock parsed = FatBootSectorCodec.Parse(sector, FatType.Fat12);

        // Assert
        parsed.BytesPerSector.Should().Be(512);
        parsed.SectorsPerCluster.Should().Be(1);
        parsed.NumberOfFats.Should().Be(2);
        parsed.RootDirEntries.Should().Be(224);
        parsed.TotalSectors16.Should().Be(2880);
        parsed.MediaDescriptor.Should().Be(0xF0);
        parsed.SectorsPerFat.Should().Be(9);
        parsed.ExtendedBootSignature.Should().Be(0x29);
    }

    [Fact]
    public void Codec_Parse_MissingMagic_ThrowsInvalidData() {
        // Arrange
        byte[] sector = BuildBootSector(NewFat12FloppyBpb(), FatType.Fat12);
        sector[510] = 0x00;
        sector[511] = 0x00;

        // Act
        Action act = () => FatBootSectorCodec.Parse(sector, FatType.Fat12);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*signature*");
    }

    [Fact]
    public void Codec_Parse_ZeroBytesPerSector_ThrowsInvalidData() {
        // Arrange
        byte[] sector = new byte[512];
        sector[510] = 0x55;
        sector[511] = 0xAA;
        // bytesPerSector left at 0.

        // Act
        Action act = () => FatBootSectorCodec.Parse(sector, FatType.Fat12);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*BytesPerSector*");
    }

    [Fact]
    public void Codec_Parse_TooShortSector_ThrowsInvalidData() {
        // Arrange
        byte[] sector = new byte[100];

        // Act
        Action act = () => FatBootSectorCodec.Parse(sector, FatType.Fat12);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Codec_Write_Fat16_RoundTrips() {
        // Arrange
        MutableBiosParameterBlock original = NewFat16Bpb();
        byte[] sector = new byte[512];

        // Act
        FatBootSectorCodec.Write(original, sector, FatType.Fat16);
        MutableBiosParameterBlock parsed = FatBootSectorCodec.Parse(sector, FatType.Fat16);

        // Assert
        parsed.BytesPerSector.Should().Be(original.BytesPerSector);
        parsed.SectorsPerCluster.Should().Be(original.SectorsPerCluster);
        parsed.ReservedSectors.Should().Be(original.ReservedSectors);
        parsed.NumberOfFats.Should().Be(original.NumberOfFats);
        parsed.RootDirEntries.Should().Be(original.RootDirEntries);
        parsed.TotalSectors16.Should().Be(original.TotalSectors16);
        parsed.MediaDescriptor.Should().Be(original.MediaDescriptor);
        parsed.SectorsPerFat.Should().Be(original.SectorsPerFat);
        parsed.SectorsPerTrack.Should().Be(original.SectorsPerTrack);
        parsed.NumberOfHeads.Should().Be(original.NumberOfHeads);
        parsed.HiddenSectors.Should().Be(original.HiddenSectors);
        sector[510].Should().Be(0x55);
        sector[511].Should().Be(0xAA);
    }

    [Fact]
    public void Codec_Write_Fat32_RoundTrips() {
        // Arrange
        MutableBiosParameterBlock original = NewFat32Bpb();
        byte[] sector = new byte[512];

        // Act
        FatBootSectorCodec.Write(original, sector, FatType.Fat32);
        MutableBiosParameterBlock parsed = FatBootSectorCodec.Parse(sector, FatType.Fat32);

        // Assert
        parsed.SectorsPerFat32.Should().Be(original.SectorsPerFat32);
        parsed.RootCluster.Should().Be(original.RootCluster);
        parsed.TotalSectors32.Should().Be(original.TotalSectors32);
    }

    [Fact]
    public void Codec_Write_NullBpb_Throws() {
        // Arrange
        byte[] sector = new byte[512];

        // Act
        Action act = () => FatBootSectorCodec.Write(null!, sector, FatType.Fat12);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validator_ConsistentFat12_ReturnsNoIssues() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat12FloppyBpb();

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat12);

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validator_ConsistentFat32_ReturnsNoIssues() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat32Bpb();

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat32);

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validator_BothTotalSectorsSet_ReturnsError() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat12FloppyBpb();
        bpb.TotalSectors32 = 9999;

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat12);

        // Assert
        issues.Should().Contain(i => i.Severity == BpbValidationSeverity.Error && i.Field == nameof(MutableBiosParameterBlock.TotalSectors16));
    }

    [Fact]
    public void Validator_BothTotalSectorsZero_ReturnsError() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat12FloppyBpb();
        bpb.TotalSectors16 = 0;

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat12);

        // Assert
        issues.Should().Contain(i => i.Severity == BpbValidationSeverity.Error);
    }

    [Fact]
    public void Validator_Fat32_WithNonZeroSectorsPerFat_ReturnsError() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat32Bpb();
        bpb.SectorsPerFat = 1;

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat32);

        // Assert
        issues.Should().Contain(i => i.Field == nameof(MutableBiosParameterBlock.SectorsPerFat));
    }

    [Fact]
    public void Validator_Fat32_WithZeroSectorsPerFat32_ReturnsError() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat32Bpb();
        bpb.SectorsPerFat32 = 0;

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat32);

        // Assert
        issues.Should().Contain(i => i.Field == nameof(MutableBiosParameterBlock.SectorsPerFat32));
    }

    [Fact]
    public void Validator_Fat12_MissingRootDirEntries_ReturnsError() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat12FloppyBpb();
        bpb.RootDirEntries = 0;

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat12);

        // Assert
        issues.Should().Contain(i => i.Field == nameof(MutableBiosParameterBlock.RootDirEntries));
    }

    [Fact]
    public void Validator_SectorsPerClusterNotPowerOfTwo_ReturnsError() {
        // Arrange
        MutableBiosParameterBlock bpb = NewFat12FloppyBpb();
        bpb.SectorsPerCluster = 3;

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatBootSectorValidator.ValidateBpbConsistency(bpb, FatType.Fat12);

        // Assert
        issues.Should().Contain(i => i.Field == nameof(MutableBiosParameterBlock.SectorsPerCluster));
    }

    [Fact]
    public void Validator_NullBpb_Throws() {
        // Arrange

        // Act
        Action act = () => FatBootSectorValidator.ValidateBpbConsistency(null!, FatType.Fat12);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromReadOnly_RoundTripsKeyFields() {
        // Arrange - build a sector via the codec, parse as read-only BPB, then re-mirror as mutable.
        byte[] sector = BuildBootSector(NewFat12FloppyBpb(), FatType.Fat12);
        BiosParameterBlock readOnly = BiosParameterBlock.Parse(sector);

        // Act
        MutableBiosParameterBlock mirror = MutableBiosParameterBlock.FromReadOnly(readOnly);

        // Assert
        mirror.BytesPerSector.Should().Be(readOnly.BytesPerSector);
        mirror.SectorsPerCluster.Should().Be(readOnly.SectorsPerCluster);
        mirror.NumberOfFats.Should().Be(readOnly.NumberOfFats);
        mirror.SectorsPerFat.Should().Be(readOnly.SectorsPerFat);
        mirror.TotalSectors16.Should().Be(readOnly.TotalSectors16);
    }

    [Fact]
    public void FromReadOnly_NullSource_Throws() {
        // Arrange

        // Act
        Action act = () => MutableBiosParameterBlock.FromReadOnly(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static MutableBiosParameterBlock NewFat12FloppyBpb() {
        // Standard 1.44 MB floppy BPB.
        return new MutableBiosParameterBlock {
            BytesPerSector = 512,
            SectorsPerCluster = 1,
            ReservedSectors = 1,
            NumberOfFats = 2,
            RootDirEntries = 224,
            TotalSectors16 = 2880,
            MediaDescriptor = 0xF0,
            SectorsPerFat = 9,
            SectorsPerTrack = 18,
            NumberOfHeads = 2,
            HiddenSectors = 0,
            TotalSectors32 = 0,
            VolumeLabel = "FLOPPY LBL "
        };
    }

    private static MutableBiosParameterBlock NewFat16Bpb() {
        return new MutableBiosParameterBlock {
            BytesPerSector = 512,
            SectorsPerCluster = 8,
            ReservedSectors = 1,
            NumberOfFats = 2,
            RootDirEntries = 512,
            TotalSectors16 = 0,
            MediaDescriptor = 0xF8,
            SectorsPerFat = 256,
            SectorsPerTrack = 63,
            NumberOfHeads = 16,
            HiddenSectors = 63,
            TotalSectors32 = 1048576,
            VolumeLabel = "HDD16 VOL  "
        };
    }

    private static MutableBiosParameterBlock NewFat32Bpb() {
        return new MutableBiosParameterBlock {
            BytesPerSector = 512,
            SectorsPerCluster = 8,
            ReservedSectors = 32,
            NumberOfFats = 2,
            RootDirEntries = 0,
            TotalSectors16 = 0,
            MediaDescriptor = 0xF8,
            SectorsPerFat = 0,
            SectorsPerTrack = 63,
            NumberOfHeads = 255,
            HiddenSectors = 63,
            TotalSectors32 = 8388608,
            SectorsPerFat32 = 1024,
            RootCluster = 2,
            VolumeLabel = "FAT32 VOL  "
        };
    }

    private static byte[] BuildBootSector(MutableBiosParameterBlock bpb, FatType fatType) {
        byte[] sector = new byte[512];
        FatBootSectorCodec.Write(bpb, sector, fatType);
        return sector;
    }
}
