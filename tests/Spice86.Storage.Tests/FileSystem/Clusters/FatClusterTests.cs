namespace Spice86.Storage.Tests.FileSystem.Clusters;

using System;
using System.Collections.Generic;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.BootSector;
using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

using Xunit;

public sealed class FatClusterCodecTests {
    [Fact]
    public void WriteFat12_EvenIndex_KeepsHighNibbleOfNextByte() {
        // Arrange
        byte[] fat = new byte[6];
        fat[1] = 0xF0;

        // Act
        FatClusterCodec.Write(fat, 0, 0x123, FatType.Fat12);

        // Assert
        fat[0].Should().Be(0x23);
        fat[1].Should().Be(0xF1);
    }

    [Fact]
    public void WriteFat12_OddIndex_KeepsLowNibbleOfPriorByte() {
        // Arrange
        byte[] fat = new byte[6];
        fat[1] = 0x0A;

        // Act
        FatClusterCodec.Write(fat, 1, 0x456, FatType.Fat12);

        // Assert
        fat[1].Should().Be(0x6A);
        fat[2].Should().Be(0x45);
    }

    [Fact]
    public void WriteFat12_TwoEntriesSpanningBoundary_PacksCorrectly() {
        // Arrange
        byte[] fat = new byte[6];

        // Act
        FatClusterCodec.Write(fat, 0, 0xABC, FatType.Fat12);
        FatClusterCodec.Write(fat, 1, 0x123, FatType.Fat12);
        uint first = FatClusterCodec.Read(fat, 0, FatType.Fat12);
        uint second = FatClusterCodec.Read(fat, 1, FatType.Fat12);

        // Assert
        first.Should().Be(0xABC);
        second.Should().Be(0x123);
    }

    [Fact]
    public void ReadWriteFat16_RoundTrip() {
        // Arrange
        byte[] fat = new byte[16];

        // Act
        FatClusterCodec.Write(fat, 2, 0xFFF8, FatType.Fat16);
        FatClusterCodec.Write(fat, 3, 0x1234, FatType.Fat16);
        uint first = FatClusterCodec.Read(fat, 2, FatType.Fat16);
        uint second = FatClusterCodec.Read(fat, 3, FatType.Fat16);

        // Assert
        first.Should().Be(0xFFF8);
        second.Should().Be(0x1234);
    }

    [Fact]
    public void WriteFat32_PreservesReservedHighNibble() {
        // Arrange
        byte[] fat = new byte[16];
        fat[0] = 0x00;
        fat[1] = 0x00;
        fat[2] = 0x00;
        fat[3] = 0xF0;

        // Act
        FatClusterCodec.Write(fat, 0, 0x0FFFFFFF, FatType.Fat32);
        uint value = FatClusterCodec.Read(fat, 0, FatType.Fat32);

        // Assert
        fat[3].Should().Be(0xFF);
        value.Should().Be(0x0FFFFFFF);
    }

    [Fact]
    public void WriteFat12_ValueExceedsTwelveBits_Throws() {
        // Arrange
        byte[] fat = new byte[4];
        Action act = () => FatClusterCodec.Write(fat, 0, 0x1000, FatType.Fat12);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IsEndOfChain_RecognisesPerType() {
        // Arrange

        // Act
        bool fat12Eof = FatClusterCodec.IsEndOfChain(0xFF8, FatType.Fat12);
        bool fat12NotEof = FatClusterCodec.IsEndOfChain(0xFF7, FatType.Fat12);
        bool fat16Eof = FatClusterCodec.IsEndOfChain(0xFFF8, FatType.Fat16);
        bool fat32Eof = FatClusterCodec.IsEndOfChain(0x0FFFFFF8, FatType.Fat32);

        // Assert
        fat12Eof.Should().BeTrue();
        fat12NotEof.Should().BeFalse();
        fat16Eof.Should().BeTrue();
        fat32Eof.Should().BeTrue();
    }

    [Fact]
    public void IsBadCluster_RecognisesPerType() {
        // Arrange

        // Act
        bool fat12Bad = FatClusterCodec.IsBadCluster(0xFF7, FatType.Fat12);
        bool fat16Bad = FatClusterCodec.IsBadCluster(0xFFF7, FatType.Fat16);
        bool fat32Bad = FatClusterCodec.IsBadCluster(0x0FFFFFF7, FatType.Fat32);
        bool fat12NotBad = FatClusterCodec.IsBadCluster(0xFF8, FatType.Fat12);

        // Assert
        fat12Bad.Should().BeTrue();
        fat16Bad.Should().BeTrue();
        fat32Bad.Should().BeTrue();
        fat12NotBad.Should().BeFalse();
    }
}

public sealed class FatTableTests {
    [Fact]
    public void Constructor_TooFewClusters_Throws() {
        // Arrange
        Action act = () => new FatTable(2, FatType.Fat16);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AllocateCluster_ReturnsFirstFreeCluster() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);

        // Act
        uint allocated = table.AllocateCluster();

        // Assert
        allocated.Should().Be(2);
        table.IsEndOfChain(2).Should().BeTrue();
    }

    [Fact]
    public void AllocateCluster_SkipsAlreadyAllocated() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.SetEntry(2, 0xFFFF);

        // Act
        uint allocated = table.AllocateCluster();

        // Assert
        allocated.Should().Be(3);
    }

    [Fact]
    public void AllocateCluster_WhenFull_ThrowsInvalidOperationException() {
        // Arrange
        FatTable table = new(3, FatType.Fat16);
        table.SetEntry(2, 0xFFFF);
        Action act = () => table.AllocateCluster();

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FreeCluster_ZeroesEntry() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.MarkAsEof(2);

        // Act
        table.FreeCluster(2);

        // Assert
        table.IsFree(2).Should().BeTrue();
    }

    [Fact]
    public void FreeCluster_ReservedIndex_Throws() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        Action act = () => table.FreeCluster(1);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LinkClusters_BuildsAndFollowsChain() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.LinkClusters(3, 5);
        table.MarkAsEof(5);

        // Act
        IReadOnlyList<uint> chain = table.FollowChain(2);

        // Assert
        chain.Should().Equal(2u, 3u, 5u);
    }

    [Fact]
    public void GetChainLength_CountsCorrectly() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.LinkClusters(3, 4);
        table.MarkAsEof(4);

        // Act
        int length = table.GetChainLength(2);

        // Assert
        length.Should().Be(3);
    }

    [Fact]
    public void FollowChain_Cycle_ThrowsCorruptionException() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.LinkClusters(3, 2);
        Action act = () => table.FollowChain(2);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<FatChainCorruptionException>().WithMessage("*Cycle*");
    }

    [Fact]
    public void FollowChain_FreeEntryInsideChain_ThrowsCorruptionException() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.LinkClusters(2, 3);
        Action act = () => table.FollowChain(2);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<FatChainCorruptionException>().WithMessage("*Free cluster*");
    }

    [Fact]
    public void FollowChain_BadClusterInChain_ThrowsCorruptionException() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.SetEntry(3, FatClusterCodec.Fat16BadCluster);
        Action act = () => table.FollowChain(2);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<FatChainCorruptionException>().WithMessage("*Bad cluster*");
    }

    [Fact]
    public void FollowChain_OutOfRangeLink_ThrowsCorruptionException() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.SetEntry(3, 0x1000);
        Action act = () => table.FollowChain(2);

        // Act
        Action assertion = act;

        // Assert
        assertion.Should().Throw<FatChainCorruptionException>().WithMessage("*out-of-range*");
    }

    [Fact]
    public void FreeClusterCount_DoesNotCountReservedEntries() {
        // Arrange
        FatTable table = new(5, FatType.Fat16);

        // Act
        int freeCount = table.FreeClusterCount;

        // Assert
        freeCount.Should().Be(3);
    }

    [Fact]
    public void UsedClusterCount_TracksAllocations() {
        // Arrange
        FatTable table = new(5, FatType.Fat16);

        // Act
        table.AllocateCluster();
        table.AllocateCluster();
        int usedCount = table.UsedClusterCount;
        int freeCount = table.FreeClusterCount;

        // Assert
        usedCount.Should().Be(2);
        freeCount.Should().Be(1);
    }

    [Fact]
    public void FromBytes_AndWriteTo_RoundTripsFat12() {
        // Arrange
        FatTable table = new(8, FatType.Fat12);
        table.LinkClusters(2, 3);
        table.LinkClusters(3, 5);
        table.MarkAsEof(5);
        table.SetEntry(7, FatClusterCodec.Fat12BadCluster);

        byte[] buffer = new byte[16];

        // Act
        table.WriteTo(buffer);
        FatTable restored = FatTable.FromBytes(buffer, FatType.Fat12, 8);
        IReadOnlyList<uint> chain = restored.FollowChain(2);
        bool isBad = restored.IsBad(7);

        // Assert
        chain.Should().Equal(2u, 3u, 5u);
        isBad.Should().BeTrue();
    }
}

public sealed class FatClusterValidatorTests {
    [Fact]
    public void IsValidDataClusterIndex_RejectsReservedAndBadRanges() {
        // Arrange

        // Act
        bool zeroValid = FatClusterValidator.IsValidDataClusterIndex(0, FatType.Fat12);
        bool oneValid = FatClusterValidator.IsValidDataClusterIndex(1, FatType.Fat12);
        bool twoValid = FatClusterValidator.IsValidDataClusterIndex(2, FatType.Fat12);
        bool fat12BadValid = FatClusterValidator.IsValidDataClusterIndex(FatClusterCodec.Fat12BadCluster, FatType.Fat12);
        bool fat16BadValid = FatClusterValidator.IsValidDataClusterIndex(FatClusterCodec.Fat16BadCluster, FatType.Fat16);

        // Assert
        zeroValid.Should().BeFalse();
        oneValid.Should().BeFalse();
        twoValid.Should().BeTrue();
        fat12BadValid.Should().BeFalse();
        fat16BadValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateChain_HealthyChain_ReturnsEmpty() {
        // Arrange
        FatTable table = new(8, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.MarkAsEof(3);

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatClusterValidator.ValidateChain(table, 2);

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateChain_Cycle_ReportsErrorIssue() {
        // Arrange
        FatTable table = new(8, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.LinkClusters(3, 2);

        // Act
        IReadOnlyList<BpbValidationIssue> issues = FatClusterValidator.ValidateChain(table, 2);

        // Assert
        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(BpbValidationSeverity.Error);
        issues[0].Field.Should().Be("Chain");
    }

    [Fact]
    public void FindOrphanedClusters_ReturnsUnreachableUsedClusters() {
        // Arrange
        FatTable table = new(10, FatType.Fat16);
        table.LinkClusters(2, 3);
        table.MarkAsEof(3);
        table.LinkClusters(5, 6);
        table.MarkAsEof(6);

        // Act
        IReadOnlyList<uint> orphans = FatClusterValidator.FindOrphanedClusters(table, new uint[] { 2 });

        // Assert
        orphans.Should().Equal(5u, 6u);
    }

    [Fact]
    public void FindOrphanedClusters_BadClusterIsNotOrphan() {
        // Arrange
        FatTable table = new(8, FatType.Fat16);
        table.SetEntry(5, FatClusterCodec.Fat16BadCluster);

        // Act
        IReadOnlyList<uint> orphans = FatClusterValidator.FindOrphanedClusters(table, Array.Empty<uint>());

        // Assert
        orphans.Should().BeEmpty();
    }
}
