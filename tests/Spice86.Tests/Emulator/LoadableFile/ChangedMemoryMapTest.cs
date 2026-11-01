namespace Spice86.Tests.Emulator.LoadableFile;

using FluentAssertions;

using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.StateSerialization.CfgReload;
using Spice86.Core.Emulator.VM.Breakpoint;

using System.Text.Json;

using Xunit;

public sealed class ChangedMemoryMapTest {
    private const uint ImageStart = 0x10000;
    private const uint ImageSize = 0x100;

    private static (Memory Memory, AddressReadWriteBreakpoints Breakpoints, LoadedImageRegistry Registry,
        ChangedMemoryMap Map) CreateMap() {
        AddressReadWriteBreakpoints breakpoints = new();
        Memory memory = new(breakpoints, new Ram(A20Gate.EndOfHighMemoryArea + 1), new A20Gate(),
            new RealModeMmu386(), false);
        LoadedImageRegistry registry = new();
        ChangedMemoryMap map = new(memory, breakpoints, registry);
        return (memory, breakpoints, registry, map);
    }

    private static void RegisterImage(LoadedImageRegistry registry, uint start, uint size) =>
        RegisterImage(registry, start, size, LoadedImageKind.DosProgram);

    private static void RegisterImage(LoadedImageRegistry registry, uint start, uint size,
        LoadedImageKind kind) {
        registry.Register(new LoadedImage(kind, "TEST.COM", (ushort)(start >> 4), start, size));
    }

    [Fact]
    public void WriteOfDifferentValueMarksTheByte() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Act
        memory.UInt8[ImageStart + 0x10] = 0xAA;

        // Assert
        map.Snapshot().IsUnchanged(ImageStart + 0x10, 1).Should().BeFalse();
        map.Snapshot().IsUnchanged(ImageStart + 0x11, 1).Should().BeTrue();
    }

    [Fact]
    public void WriteOfSameValueDoesNotMarkTheByte() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Act
        memory.UInt8[ImageStart + 0x10] = 0;

        // Assert
        map.Snapshot().IsUnchanged(ImageStart, (int)ImageSize).Should().BeTrue();
    }

    [Fact]
    public void ByteChangedAndSetBackStaysMarked() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Act
        memory.UInt8[ImageStart + 0x10] = 0xAA;
        memory.UInt8[ImageStart + 0x10] = 0;

        // Assert
        map.Snapshot().IsUnchanged(ImageStart + 0x10, 1).Should().BeFalse();
    }

    [Fact]
    public void WriteOutsideWatchedAreasIsIgnored() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Act
        memory.UInt8[0x40000] = 0xAA;

        // Assert
        map.Snapshot().IsUnchanged(0x40000, 1).Should().BeFalse("bytes outside the watched areas are never unchanged");
        map.Snapshot().ChangedRanges.Should().BeEmpty();
    }

    [Fact]
    public void IsUnchangedIsFalseWhenTheRangeLeavesTheWatchedAreas() {
        // Arrange / Act
        (Memory _, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Assert
        map.Snapshot().IsUnchanged(ImageStart + ImageSize - 1, 2).Should().BeFalse();
    }

    [Fact]
    public void SecondImageNextToTheFirstMergesIntoOneArea() {
        // Arrange / Act
        (_, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);
        RegisterImage(registry, ImageStart + ImageSize, ImageSize, LoadedImageKind.DosOverlay);

        // Assert
        map.Snapshot().WatchedAreas.Should().Equal((ImageStart, ImageStart + 2 * ImageSize));
    }

    [Fact]
    public void LoadIntoAnAlreadyWatchedAreaMarksTheBytesItChanges() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);
        map.Snapshot().ChangedRanges.Should().BeEmpty("the loader's own writes are never counted");

        // Act
        byte[] secondLoad = new byte[ImageSize];
        secondLoad[0x20] = 0xBB;
        memory.LoadData(ImageStart, secondLoad);

        // Assert
        map.Snapshot().IsUnchanged(ImageStart + 0x20, 1).Should().BeFalse();
        map.Snapshot().IsUnchanged(ImageStart, 0x20).Should().BeTrue();
    }

    [Fact]
    public void MergedRangeExportCoalescesContiguousChangedBytes() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Act
        memory.UInt8[ImageStart + 0x10] = 1;
        memory.UInt8[ImageStart + 0x12] = 1;

        // Assert
        ChangedMemorySnapshot snapshot = map.Snapshot();
        snapshot.ChangedRanges.Should().Equal(
            (ImageStart + 0x10, ImageStart + 0x11),
            (ImageStart + 0x12, ImageStart + 0x13));

        memory.UInt8[ImageStart + 0x11] = 1;

        map.Snapshot().ChangedRanges.Should().Equal((ImageStart + 0x10, ImageStart + 0x13));
    }

    [Fact]
    public void UnmonitoredWritesIntoAWatchedRangeDoNotMarkTheMap() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Act: WriteRam and SneakilyWrite bypass write breakpoints (user edits and test
        // resets), so a byte they change is not the program changing it.
        memory.WriteRam([0xCC], ImageStart + 0x40);
        memory.SneakilyWrite(ImageStart + 0x50, 0xCC);

        // Assert
        map.Snapshot().IsUnchanged(ImageStart + 0x40, 1).Should().BeTrue();
        map.Snapshot().IsUnchanged(ImageStart + 0x50, 1).Should().BeTrue();
        map.Snapshot().ChangedRanges.Should().BeEmpty();
    }

    [Fact]
    public void SnapshotSurvivesAReloadDumpRoundTrip() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);
        memory.UInt8[ImageStart + 0x10] = 1;
        memory.UInt8[ImageStart + 0x20] = 2;
        CfgReloadDump dump = new() {
            IdAllocatorNext = 0,
            EntryPoints = [],
            Nodes = [],
            Edges = [],
            Blocks = [],
            WatchedAreas = ChangedMemorySnapshot.SerializeRanges(map.Snapshot().WatchedAreas),
            ChangedRanges = ChangedMemorySnapshot.SerializeRanges(map.Snapshot().ChangedRanges)
        };

        // Act
        string json = JsonSerializer.Serialize(dump, CfgReloadSerialization.Options);
        CfgReloadDump read = JsonSerializer.Deserialize<CfgReloadDump>(json,
            CfgReloadSerialization.Options) ?? throw new InvalidOperationException("The dump did not deserialize.");

        // Assert
        (_, _, _, ChangedMemoryMap restored) = CreateMap();
        restored.Restore(ChangedMemorySnapshot.FromReloadDump(read));

        restored.Snapshot().Should().BeEquivalentTo(map.Snapshot());
    }

    [Fact]
    public void DifferentValueWriteDisarmsTheByteWhileSameValueWriteKeepsItArmed() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);

        // Act
        memory.UInt8[ImageStart + 0x10] = 0;
        memory.UInt8[ImageStart + 0x20] = 0xAA;

        // Assert
        map.IsArmed(ImageStart + 0x10).Should().BeTrue();
        map.IsArmed(ImageStart + 0x20).Should().BeFalse();
    }

    [Fact]
    public void OverlappingImageDoesNotReArmChangedBytesAndExtendsTheWatchedArea() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, 0x10000, 0x100);
        memory.UInt8[0x10010] = 0xAA;
        memory.UInt8[0x10090] = 0xBB;
        memory.UInt8[0x10180] = 0xCC;

        // Act
        RegisterImage(registry, 0x10080, 0x180, LoadedImageKind.DosOverlay);

        // Assert
        map.IsArmed(0x10010).Should().BeFalse();
        map.IsArmed(0x10090).Should().BeFalse();
        map.Snapshot().IsUnchanged(0x10100, 0x100).Should().BeTrue();
        map.IsArmed(0x10180).Should().BeTrue();
        map.Snapshot().WatchedAreas.Should().Equal((0x10000u, 0x10200u));
    }

    [Fact]
    public void ReRegisteredImageAfterSameValueWritesStaysUnchangedAndArmed() {
        // Arrange
        (Memory memory, _, LoadedImageRegistry registry, ChangedMemoryMap map) = CreateMap();
        RegisterImage(registry, ImageStart, ImageSize);
        memory.LoadData(ImageStart, new byte[ImageSize]);

        // Act
        RegisterImage(registry, ImageStart, ImageSize);

        // Assert
        map.Snapshot().IsUnchanged(ImageStart, (int)ImageSize).Should().BeTrue();
        map.IsArmed(ImageStart).Should().BeTrue();
        map.IsArmed(ImageStart + ImageSize - 1).Should().BeTrue();
    }

    [Fact]
    public void RestoreMarksChangedBytesAndKeepsTheRestArmed() {
        // Arrange
        (Memory memory, _, _, ChangedMemoryMap map) = CreateMap();
        ChangedMemorySnapshot snapshot = new() {
            WatchedAreas = [(0x10000u, 0x10100u)],
            ChangedRanges = [(0x10010u, 0x10012u)]
        };

        // Act
        map.Restore(snapshot);

        // Assert
        map.IsArmed(0x10010).Should().BeFalse();
        map.IsArmed(0x10020).Should().BeTrue();

        memory.UInt8[0x10020] = 0xAA;

        map.Snapshot().IsUnchanged(0x10020, 1).Should().BeFalse();
        map.IsArmed(0x10020).Should().BeFalse();
    }
}
