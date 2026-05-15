namespace Spice86.Storage.Tests.FileSystem.Directory.LongFileName;

using System.Collections.Generic;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;
using Spice86.Shared.Emulator.Storage.FileSystem.Directory;
using Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

using Xunit;

/// <summary>
/// Integration tests for <see cref="DirectoryWriter"/>'s LFN write path:
/// emitted chains must round-trip through the <see cref="VfatDirectoryReader"/>.
/// </summary>
public sealed class DirectoryWriterLfnTests
{
    /// <summary>A two-slot long name plus its 8.3 entry round-trip through the reader.</summary>
    [Fact]
    public void WriteEntryWithLongName_TwoSlotName_RoundTripsThroughReader()
    {
        // Arrange
        DirectoryWriter writer = new();
        FatTable fatTable = new(64, FatType.Fat16);
        byte[] directory = new byte[FatDirectoryEntry.EntrySize * 16];
        MutableFatDirectoryEntry entry = new()
        {
            BaseName = "MYDOCU~1",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 4,
            FileSize = 1234,
            LongName = "My Long Document Name.txt"
        };

        // Act
        writer.WriteEntryWithLongName(directory, entry, fatTable);
        VfatDirectoryReader reader = new();
        IReadOnlyList<VfatDirectoryRecord> records = reader.ReadRecords(directory);

        // Assert
        records.Should().HaveCount(1);
        records[0].LongName.Should().Be("My Long Document Name.txt");
        records[0].ShortEntry.BaseName.Should().Be("MYDOCU~1");
        records[0].ShortEntry.Extension.Should().Be("TXT");
        records[0].ShortEntry.FirstCluster.Should().Be(4);
        records[0].ShortEntry.FileSize.Should().Be(1234);
    }

    /// <summary>A single-slot name still emits one LFN slot before the short entry.</summary>
    [Fact]
    public void WriteEntryWithLongName_SingleSlotName_EmitsOneLfnPlusShortEntry()
    {
        // Arrange
        DirectoryWriter writer = new();
        FatTable fatTable = new(64, FatType.Fat16);
        byte[] directory = new byte[FatDirectoryEntry.EntrySize * 8];
        MutableFatDirectoryEntry entry = new()
        {
            BaseName = "HELLO~1",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 2,
            FileSize = 12,
            LongName = "Hello.txt"
        };

        // Act
        writer.WriteEntryWithLongName(directory, entry, fatTable);

        // Assert
        // Slot 0 must be the LFN (attribute 0x0F), slot 1 the short entry, slot 2 still empty.
        directory[0 * FatDirectoryEntry.EntrySize + 11].Should().Be(VfatLfnEntry.LfnAttribute);
        directory[1 * FatDirectoryEntry.EntrySize + 11].Should().Be(0x20);
        directory[2 * FatDirectoryEntry.EntrySize + 0].Should().Be(0x00);
    }

    /// <summary>Writing past existing entries reuses contiguous free slots after the populated range.</summary>
    [Fact]
    public void WriteEntryWithLongName_SkipsPopulatedEntriesAndFindsContiguousRun()
    {
        // Arrange
        DirectoryWriter writer = new();
        FatTable fatTable = new(64, FatType.Fat16);
        byte[] directory = new byte[FatDirectoryEntry.EntrySize * 16];
        MutableFatDirectoryEntry firstEntry = new()
        {
            BaseName = "FIRST",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 2,
            FileSize = 1
        };
        firstEntry.Serialize(directory.AsSpan(0, FatDirectoryEntry.EntrySize));
        MutableFatDirectoryEntry entry = new()
        {
            BaseName = "READM~1",
            Extension = "MD",
            Attributes = 0x20,
            FirstCluster = 8,
            FileSize = 99,
            LongName = "Read Me Now.md"
        };

        // Act
        writer.WriteEntryWithLongName(directory, entry, fatTable);
        VfatDirectoryReader reader = new();
        IReadOnlyList<VfatDirectoryRecord> records = reader.ReadRecords(directory);

        // Assert
        records.Should().HaveCount(2);
        records[0].LongName.Should().BeNull();
        records[0].ShortEntry.BaseName.Should().Be("FIRST");
        records[1].LongName.Should().Be("Read Me Now.md");
        records[1].ShortEntry.FirstCluster.Should().Be(8);
    }

    /// <summary>A null or empty long name falls back to the legacy write path.</summary>
    [Fact]
    public void WriteEntryWithLongName_EmptyLongName_WritesShortEntryOnly()
    {
        // Arrange
        DirectoryWriter writer = new();
        FatTable fatTable = new(64, FatType.Fat16);
        byte[] directory = new byte[FatDirectoryEntry.EntrySize * 4];
        MutableFatDirectoryEntry entry = new()
        {
            BaseName = "PLAIN",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 5,
            FileSize = 10,
            LongName = string.Empty
        };

        // Act
        writer.WriteEntryWithLongName(directory, entry, fatTable);

        // Assert
        directory[0 * FatDirectoryEntry.EntrySize + 11].Should().Be(0x20);
        directory[1 * FatDirectoryEntry.EntrySize + 0].Should().Be(0x00);
    }
}
