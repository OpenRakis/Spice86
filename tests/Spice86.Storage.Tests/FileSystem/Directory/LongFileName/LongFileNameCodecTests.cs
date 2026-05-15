namespace Spice86.Storage.Tests.FileSystem.Directory.LongFileName;

using System;
using System.Collections.Generic;
using System.Text;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.Directory;
using Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

using Xunit;

/// <summary>
/// Behavioural tests for the VFAT Long File Name codec and paired directory reader.
/// </summary>
public sealed class LongFileNameCodecTests
{
    /// <summary>Verifies the canonical RFC-style VFAT checksum for "MYFILE  TXT".</summary>
    [Fact]
    public void ComputeShortNameChecksum_KnownVector_MatchesReferenceValue()
    {
        // Arrange
        LongFileNameCodec codec = new();
        byte[] padded = Encoding.ASCII.GetBytes("MYFILE  TXT");

        // Act
        byte checksum = codec.ComputeShortNameChecksum(padded);

        // Assert
        checksum.Should().Be(0xBA);
    }

    /// <summary>An 11-char-long short-name input is required by the checksum routine.</summary>
    [Fact]
    public void ComputeShortNameChecksum_WrongInputLength_Throws()
    {
        // Arrange
        LongFileNameCodec codec = new();
        byte[] tooShort = new byte[5];

        // Act
        Action act = () => codec.ComputeShortNameChecksum(tooShort);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A short long-name (<=13 chars) encodes to a single LFN slot with IsLast set.</summary>
    [Fact]
    public void EncodeLongName_NineChars_ReturnsSingleSlotWithLastFlag()
    {
        // Arrange
        LongFileNameCodec codec = new();
        string longName = "hello.txt";

        // Act
        IReadOnlyList<VfatLfnEntry> slots = codec.EncodeLongName(longName, shortNameChecksum: 0xAB);

        // Assert
        slots.Should().HaveCount(1);
        slots[0].Ordinal.Should().Be(1);
        slots[0].IsLast.Should().BeTrue();
        slots[0].Checksum.Should().Be(0xAB);
        slots[0].NameFragment[0].Should().Be('h');
        slots[0].NameFragment[8].Should().Be('t');
        slots[0].NameFragment[9].Should().Be('\u0000');
        slots[0].NameFragment[10].Should().Be('\uFFFF');
    }

    /// <summary>A 14-char name spans two slots ordered topmost-first.</summary>
    [Fact]
    public void EncodeLongName_FourteenChars_ReturnsTwoSlotsTopmostFirst()
    {
        // Arrange
        LongFileNameCodec codec = new();
        string longName = "abcdefghijklmn";

        // Act
        IReadOnlyList<VfatLfnEntry> slots = codec.EncodeLongName(longName, shortNameChecksum: 0x11);

        // Assert
        slots.Should().HaveCount(2);
        // Disc order: topmost (ordinal 2, last) first; ordinal 1 second.
        slots[0].Ordinal.Should().Be(2);
        slots[0].IsLast.Should().BeTrue();
        slots[0].NameFragment[0].Should().Be('n');
        slots[0].NameFragment[1].Should().Be('\u0000');
        slots[1].Ordinal.Should().Be(1);
        slots[1].IsLast.Should().BeFalse();
        slots[1].NameFragment[0].Should().Be('a');
        slots[1].NameFragment[12].Should().Be('m');
    }

    /// <summary>An exactly-13-char name fits in one slot without an internal NUL terminator.</summary>
    [Fact]
    public void EncodeLongName_ExactlyThirteenChars_NoInternalNullTerminator()
    {
        // Arrange
        LongFileNameCodec codec = new();
        string longName = "abcdefghijklm";

        // Act
        IReadOnlyList<VfatLfnEntry> slots = codec.EncodeLongName(longName, shortNameChecksum: 0x00);

        // Assert
        slots.Should().HaveCount(1);
        for (int i = 0; i < 13; i++)
        {
            slots[0].NameFragment[i].Should().Be((char)('a' + i));
        }
    }

    /// <summary>Slot bytes round-trip through WriteSlot + TryParseSlot.</summary>
    [Fact]
    public void WriteSlot_AndTryParseSlot_RoundTripPreservesAllFields()
    {
        // Arrange
        LongFileNameCodec codec = new();
        IReadOnlyList<VfatLfnEntry> slots = codec.EncodeLongName("hello-world.x", shortNameChecksum: 0x7F);
        VfatLfnEntry original = slots[0];
        byte[] buffer = new byte[FatDirectoryEntry.EntrySize];

        // Act
        codec.WriteSlot(original, buffer);
        VfatLfnEntry? parsed = codec.TryParseSlot(buffer);

        // Assert
        parsed.Should().NotBeNull();
        parsed!.Ordinal.Should().Be(original.Ordinal);
        parsed.IsLast.Should().Be(original.IsLast);
        parsed.Checksum.Should().Be(original.Checksum);
        parsed.NameFragment.Should().BeEquivalentTo(original.NameFragment);
        buffer[11].Should().Be(VfatLfnEntry.LfnAttribute);
    }

    /// <summary>A non-LFN entry (attribute byte != 0x0F) is not interpreted as a slot.</summary>
    [Fact]
    public void TryParseSlot_NonLfnAttribute_ReturnsNull()
    {
        // Arrange
        LongFileNameCodec codec = new();
        byte[] buffer = new byte[FatDirectoryEntry.EntrySize];
        buffer[11] = 0x20; // archive attribute

        // Act
        VfatLfnEntry? parsed = codec.TryParseSlot(buffer);

        // Assert
        parsed.Should().BeNull();
    }

    /// <summary>A well-formed chain decodes back to the original long name.</summary>
    [Fact]
    public void DecodeLongName_WellFormedChain_RecoversOriginalName()
    {
        // Arrange
        LongFileNameCodec codec = new();
        string original = "very long file name.txt";
        byte checksum = 0x42;
        IReadOnlyList<VfatLfnEntry> slots = codec.EncodeLongName(original, checksum);

        // Act
        string? decoded = codec.DecodeLongName(slots, checksum);

        // Assert
        decoded.Should().Be(original);
    }

    /// <summary>A checksum mismatch causes decode to return null (chain is rejected).</summary>
    [Fact]
    public void DecodeLongName_ChecksumMismatch_ReturnsNull()
    {
        // Arrange
        LongFileNameCodec codec = new();
        IReadOnlyList<VfatLfnEntry> slots = codec.EncodeLongName("readme.txt", shortNameChecksum: 0x10);

        // Act
        string? decoded = codec.DecodeLongName(slots, expectedChecksum: 0x99);

        // Assert
        decoded.Should().BeNull();
    }

    /// <summary>A chain whose topmost slot does not carry the last-flag is rejected.</summary>
    [Fact]
    public void DecodeLongName_TopmostSlotMissingLastFlag_ReturnsNull()
    {
        // Arrange
        LongFileNameCodec codec = new();
        char[] fragment = new char[VfatLfnEntry.CharsPerSlot];
        for (int i = 0; i < 13; i++)
        {
            fragment[i] = 'A';
        }
        VfatLfnEntry brokenTop = new VfatLfnEntry(ordinal: 1, isLast: false, checksum: 0x00, nameFragment: fragment);
        List<VfatLfnEntry> chain = new() { brokenTop };

        // Act
        string? decoded = codec.DecodeLongName(chain, expectedChecksum: 0x00);

        // Assert
        decoded.Should().BeNull();
    }

    /// <summary>The reader pairs preceding LFN slots with the next 8.3 short entry.</summary>
    [Fact]
    public void ReadRecords_LfnChainPrecedingShortEntry_PairsThemTogether()
    {
        // Arrange
        VfatDirectoryReader reader = new();
        LongFileNameCodec codec = new();
        MutableFatDirectoryEntry shortEntry = new MutableFatDirectoryEntry
        {
            BaseName = "MYFILE",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 5,
            FileSize = 42
        };
        byte checksum = codec.ComputeShortNameChecksum(Encoding.ASCII.GetBytes("MYFILE  TXT"));
        IReadOnlyList<VfatLfnEntry> slots = codec.EncodeLongName("My File With A Long Name.txt", checksum);
        byte[] buffer = new byte[FatDirectoryEntry.EntrySize * (slots.Count + 2)];
        for (int i = 0; i < slots.Count; i++)
        {
            codec.WriteSlot(slots[i], buffer.AsSpan(i * FatDirectoryEntry.EntrySize, FatDirectoryEntry.EntrySize));
        }
        int shortOffset = slots.Count * FatDirectoryEntry.EntrySize;
        shortEntry.Serialize(buffer.AsSpan(shortOffset, FatDirectoryEntry.EntrySize));

        // Act
        IReadOnlyList<VfatDirectoryRecord> records = reader.ReadRecords(buffer);

        // Assert
        records.Should().HaveCount(1);
        records[0].ShortEntry.DosName.Should().Be("MYFILE.TXT");
        records[0].LongName.Should().Be("My File With A Long Name.txt");
    }

    /// <summary>A standalone 8.3 entry has a null LongName when no LFN slots precede it.</summary>
    [Fact]
    public void ReadRecords_ShortEntryWithNoLfn_YieldsRecordWithNullLongName()
    {
        // Arrange
        VfatDirectoryReader reader = new();
        MutableFatDirectoryEntry shortEntry = new MutableFatDirectoryEntry
        {
            BaseName = "PLAIN",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 3,
            FileSize = 7
        };
        byte[] buffer = new byte[FatDirectoryEntry.EntrySize * 2];
        shortEntry.Serialize(buffer.AsSpan(0, FatDirectoryEntry.EntrySize));

        // Act
        IReadOnlyList<VfatDirectoryRecord> records = reader.ReadRecords(buffer);

        // Assert
        records.Should().HaveCount(1);
        records[0].LongName.Should().BeNull();
    }

    /// <summary>A deleted entry (0xE5) discards any LFN slots accumulated before it.</summary>
    [Fact]
    public void ReadRecords_DeletedShortEntry_DropsPrecedingLfnSlots()
    {
        // Arrange
        VfatDirectoryReader reader = new();
        LongFileNameCodec codec = new();
        byte checksum = 0x55;
        IReadOnlyList<VfatLfnEntry> dropped = codec.EncodeLongName("orphan.txt", checksum);
        MutableFatDirectoryEntry kept = new MutableFatDirectoryEntry
        {
            BaseName = "AFTER",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 9,
            FileSize = 1
        };
        byte[] buffer = new byte[FatDirectoryEntry.EntrySize * (dropped.Count + 2)];
        for (int i = 0; i < dropped.Count; i++)
        {
            codec.WriteSlot(dropped[i], buffer.AsSpan(i * FatDirectoryEntry.EntrySize, FatDirectoryEntry.EntrySize));
        }
        int deletedOffset = dropped.Count * FatDirectoryEntry.EntrySize;
        buffer[deletedOffset] = VfatDirectoryReader.DeletedEntryMarker;
        buffer[deletedOffset + 11] = 0x20;
        kept.Serialize(buffer.AsSpan(deletedOffset + FatDirectoryEntry.EntrySize, FatDirectoryEntry.EntrySize));

        // Act
        IReadOnlyList<VfatDirectoryRecord> records = reader.ReadRecords(buffer);

        // Assert
        records.Should().HaveCount(1);
        records[0].ShortEntry.DosName.Should().Be("AFTER.TXT");
        records[0].LongName.Should().BeNull();
    }

    /// <summary>The end-of-directory marker (0x00) stops enumeration immediately.</summary>
    [Fact]
    public void ReadRecords_EndOfDirectoryMarker_StopsEnumeration()
    {
        // Arrange
        VfatDirectoryReader reader = new();
        MutableFatDirectoryEntry first = new MutableFatDirectoryEntry
        {
            BaseName = "FIRST",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 2,
            FileSize = 1
        };
        MutableFatDirectoryEntry afterEod = new MutableFatDirectoryEntry
        {
            BaseName = "GHOST",
            Extension = "TXT",
            Attributes = 0x20,
            FirstCluster = 4,
            FileSize = 1
        };
        byte[] buffer = new byte[FatDirectoryEntry.EntrySize * 3];
        first.Serialize(buffer.AsSpan(0, FatDirectoryEntry.EntrySize));
        // buffer[32] = 0x00 already (end-of-directory marker).
        afterEod.Serialize(buffer.AsSpan(64, FatDirectoryEntry.EntrySize));

        // Act
        IReadOnlyList<VfatDirectoryRecord> records = reader.ReadRecords(buffer);

        // Assert
        records.Should().HaveCount(1);
        records[0].ShortEntry.DosName.Should().Be("FIRST.TXT");
    }
}
