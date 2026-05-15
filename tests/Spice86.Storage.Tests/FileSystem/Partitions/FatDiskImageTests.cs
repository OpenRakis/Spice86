namespace Spice86.Storage.Tests.FileSystem.Partitions;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

using Xunit;

public sealed class MbrCodecTests
{
    [Fact]
    public void MbrCodec_Parse_MissingMagic_ThrowsInvalidDataException()
    {
        // Arrange
        byte[] mbrSector = new byte[512];
        mbrSector[510] = 0x00;
        mbrSector[511] = 0x00;
        Action act = () => MbrCodec.Parse(mbrSector);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MasterBootRecord_Serialize_RoundTrip()
    {
        // Arrange
        List<PartitionTableEntry> partitions = new List<PartitionTableEntry> {
            new PartitionTableEntry(0x80, 0x01, 1, 2880),
            new PartitionTableEntry(0x00, 0x06, 4000, 2000)
        };
        MasterBootRecord original = new MasterBootRecord(partitions);
        byte[] mbrSector = new byte[512];

        // Act
        MbrCodec.Write(original, mbrSector);
        MasterBootRecord parsed = MbrCodec.Parse(mbrSector);

        // Assert
        parsed.Partitions.Should().HaveCount(4);
        parsed.Partitions[0].BootIndicator.Should().Be(0x80);
        parsed.Partitions[0].PartitionType.Should().Be(0x01);
        parsed.Partitions[0].LbaStart.Should().Be(1u);
        parsed.Partitions[0].SectorCount.Should().Be(2880u);
        parsed.Partitions[1].PartitionType.Should().Be(0x06);
        parsed.ValidateMagic(mbrSector).Should().BeTrue();
    }
}

public sealed class PartitionTableValidatorTests
{
    [Fact]
    public void PartitionTableValidator_OverlappingPartitions_ReturnsIssue()
    {
        // Arrange
        MasterBootRecord mbr = new MasterBootRecord(new[] {
            new PartitionTableEntry(0x00, 0x06, 1, 100),
            new PartitionTableEntry(0x00, 0x06, 50, 100)
        });

        // Act
        IReadOnlyList<PartitionValidationIssue> issues = PartitionTableValidator.ValidatePartitions(mbr);

        // Assert
        issues.Should().Contain(i => i.Severity == PartitionValidationSeverity.Error);
    }
}

public sealed class FatDiskImageTests : IDisposable
{
    private readonly string _tempDirectory;

    public FatDiskImageTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Spice86.Storage.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "HELLO.TXT"), "HELLO", Encoding.ASCII);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void FatDiskImage_OpenRaw_IgnoresMbrAndReadsFat()
    {
        // Arrange
        VirtualFloppyImage imageBuilder = new VirtualFloppyImage(_tempDirectory, logger: null);
        byte[] rawFat = imageBuilder.Build();

        // Act
        FatDiskImage diskImage = FatDiskImage.Open(rawFat);
        FatFileSystem fileSystem = diskImage.GetBootableFilesystem();
        bool exists = fileSystem.Exists("HELLO.TXT");

        // Assert
        diskImage.Partitions.Should().HaveCount(1);
        exists.Should().BeTrue();
    }

    [Fact]
    public void FatDiskImage_OpenPartitioned_ReadsFatFromPartitionOffset()
    {
        // Arrange
        VirtualFloppyImage imageBuilder = new VirtualFloppyImage(_tempDirectory, logger: null);
        byte[] rawFat = imageBuilder.Build();
        byte[] disk = BuildPartitionedDisk(rawFat, lbaStart: 1, bootIndicator: 0x00);

        // Act
        FatDiskImage diskImage = FatDiskImage.Open(disk);
        FatFileSystem bootable = diskImage.GetBootableFilesystem();

        // Assert
        diskImage.Partitions.Should().HaveCount(1);
        bootable.Exists("HELLO.TXT").Should().BeTrue();
    }

    [Fact]
    public void FatDiskImage_GetBootableFilesystem_ChoosesBootIndicator()
    {
        // Arrange
        VirtualFloppyImage imageBuilder = new VirtualFloppyImage(_tempDirectory, logger: null);
        byte[] rawFat = imageBuilder.Build();

        byte[] disk = new byte[rawFat.Length + 4096];
        MasterBootRecord mbr = new MasterBootRecord(new[] {
            new PartitionTableEntry(0x00, 0x06, 1, (uint)(rawFat.Length / 512)),
            new PartitionTableEntry(0x80, 0x06, 5, (uint)(rawFat.Length / 512))
        });

        MbrCodec.Write(mbr, disk.AsSpan(0, 512));
        Array.Copy(rawFat, 0, disk, 1 * 512, rawFat.Length);
        Array.Copy(rawFat, 0, disk, 5 * 512, rawFat.Length);

        // Act
        FatDiskImage diskImage = FatDiskImage.Open(disk);
        FatFileSystem bootable = diskImage.GetBootableFilesystem();

        // Assert
        diskImage.Partitions.Should().HaveCount(2);
        bootable.Exists("HELLO.TXT").Should().BeTrue();
    }

    [Fact]
    public void FatDiskImage_GetBootableFilesystem_ChoosesFirstNonEmptyIfNoBootable()
    {
        // Arrange
        VirtualFloppyImage imageBuilder = new VirtualFloppyImage(_tempDirectory, logger: null);
        byte[] rawFat = imageBuilder.Build();
        byte[] disk = BuildPartitionedDisk(rawFat, lbaStart: 2, bootIndicator: 0x00);

        // Act
        FatDiskImage diskImage = FatDiskImage.Open(disk);
        FatFileSystem bootable = diskImage.GetBootableFilesystem();

        // Assert
        diskImage.Partitions.Should().HaveCount(1);
        bootable.Exists("HELLO.TXT").Should().BeTrue();
    }

    private static byte[] BuildPartitionedDisk(byte[] rawFat, uint lbaStart, byte bootIndicator)
    {
        int totalSectors = (int)lbaStart + (rawFat.Length / 512) + 8;
        byte[] disk = new byte[totalSectors * 512];

        MasterBootRecord mbr = new MasterBootRecord(new[] {
            new PartitionTableEntry(bootIndicator, 0x01, lbaStart, (uint)(rawFat.Length / 512))
        });

        MbrCodec.Write(mbr, disk.AsSpan(0, 512));
        Array.Copy(rawFat, 0, disk, (int)lbaStart * 512, rawFat.Length);
        return disk;
    }
}
