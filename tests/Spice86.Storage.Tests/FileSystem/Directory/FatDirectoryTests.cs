namespace Spice86.Storage.Tests.FileSystem.Directory;

using System;
using System.Collections.Generic;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;
using Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using Xunit;

public sealed class DosNameConverterTests
{
    [Fact]
    public void DosNameConverter_LongNameWith8Chars_ConvertsTo8_3()
    {
        // Arrange
        string sourceName = "longfilename.txt";

        // Act
        string converted = DosNameConverter.Convert(sourceName);

        // Assert
        converted.Should().Be("LONGFILE.TXT");
    }

    [Fact]
    public void DosNameConverter_LowerCase_ConvertsToUpperCase()
    {
        // Arrange
        string sourceName = "readme.doc";

        // Act
        string converted = DosNameConverter.Convert(sourceName);

        // Assert
        converted.Should().Be("README.DOC");
    }

    [Fact]
    public void DosNameConverter_InvalidChars_ThrowsArgumentException()
    {
        // Arrange
        string sourceName = "bad?name.txt";
        Action act = () => DosNameConverter.Convert(sourceName);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DosNameConverter_IsDosCompatible_RecognisesCompatibility()
    {
        // Arrange
        string compatible = "HELLO.TXT";
        string incompatible = "very-long-name.txt";

        // Act
        bool compatibleResult = DosNameConverter.IsDosCompatible(compatible);
        bool incompatibleResult = DosNameConverter.IsDosCompatible(incompatible);

        // Assert
        compatibleResult.Should().BeTrue();
        incompatibleResult.Should().BeFalse();
    }
}

public sealed class MutableFatDirectoryEntryTests
{
    [Fact]
    public void MutableFatDirectoryEntry_Serialize_RoundTrip()
    {
        // Arrange
        MutableFatDirectoryEntry original = new MutableFatDirectoryEntry
        {
            BaseName = "README",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 12,
            FileSize = 3456
        };

        byte[] buffer = new byte[FatDirectoryEntry.EntrySize];

        // Act
        original.Serialize(buffer);
        MutableFatDirectoryEntry parsed = MutableFatDirectoryEntry.Parse(buffer);

        // Assert
        parsed.BaseName.Should().Be("README");
        parsed.Extension.Should().Be("TXT");
        parsed.Attributes.Should().Be(0x20);
        parsed.FirstCluster.Should().Be(12);
        parsed.FileSize.Should().Be(3456);
    }

    [Fact]
    public void MutableFatDirectoryEntry_Serialize_InvalidBaseName_Throws()
    {
        // Arrange
        MutableFatDirectoryEntry entry = new MutableFatDirectoryEntry
        {
            BaseName = "TOO-LONG-NAME",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 2,
            FileSize = 1
        };

        byte[] buffer = new byte[FatDirectoryEntry.EntrySize];
        Action act = () => entry.Serialize(buffer);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<ArgumentException>();
    }
}

public sealed class FileAllocationStrategyTests
{
    [Fact]
    public void FileAllocationStrategy_FirstFit_FillsFragmentedSpace()
    {
        // Arrange
        FatTable table = new FatTable(12, FatType.Fat16);
        table.SetEntry(3, 0xFFFF);
        table.SetEntry(6, 0xFFFF);
        FirstFitAllocationStrategy strategy = new FirstFitAllocationStrategy(512);

        // Act
        IReadOnlyList<uint> allocated = strategy.Allocate(3 * 512, table);

        // Assert
        allocated.Should().Equal(2u, 4u, 5u);
        table[2].Should().Be(4u);
        table[4].Should().Be(5u);
        table.IsEndOfChain(5).Should().BeTrue();
    }

    [Fact]
    public void FileAllocationStrategy_Contiguous_RejectsFragmentedOnlyFreeSpace()
    {
        // Arrange
        FatTable table = new FatTable(8, FatType.Fat16);
        table.SetEntry(3, 0xFFFF);
        table.SetEntry(5, 0xFFFF);
        ContiguousAllocationStrategy strategy = new ContiguousAllocationStrategy(512);
        Action act = () => strategy.Allocate(3 * 512, table);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<InvalidOperationException>();
    }
}

public sealed class DirectoryWriterTests
{
    [Fact]
    public void DirectoryWriter_FindNextSlot_SkipsDeletedEntry()
    {
        // Arrange
        DirectoryWriter writer = new DirectoryWriter();
        byte[] directory = new byte[FatDirectoryEntry.EntrySize * 4];
        directory[0] = (byte)'A';
        directory[FatDirectoryEntry.EntrySize] = FatDirectoryEntry.DeletedEntry;

        // Act
        int slot = writer.FindNextSlot(directory);

        // Assert
        slot.Should().Be(1);
    }

    [Fact]
    public void DirectoryWriter_WriteEntry_UpdatesFatAndDirSector()
    {
        // Arrange
        DirectoryWriter writer = new DirectoryWriter();
        byte[] directory = new byte[FatDirectoryEntry.EntrySize * 4];
        FatTable table = new FatTable(20, FatType.Fat16);
        table.SetEntry(7, 8);

        MutableFatDirectoryEntry entry = new MutableFatDirectoryEntry
        {
            BaseName = "FILE",
            Extension = "BIN",
            Attributes = 0x20,
            FirstCluster = 7,
            FileSize = 600
        };

        // Act
        writer.WriteEntry(directory, 0, entry, table);
        MutableFatDirectoryEntry parsed = MutableFatDirectoryEntry.Parse(directory.AsSpan(0, FatDirectoryEntry.EntrySize));

        // Assert
        parsed.DosName.Should().Be("FILE.BIN");
        parsed.FileSize.Should().Be(600);
        table.IsEndOfChain(7).Should().BeTrue();
    }
}
