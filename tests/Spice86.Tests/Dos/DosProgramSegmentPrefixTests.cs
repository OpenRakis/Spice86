namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// Verifies that <see cref="DosProgramSegmentPrefix"/> behaves correctly as a memory view/wrapper.
/// </summary>
public class DosProgramSegmentPrefixTests {
    private static Memory CreateMemory() {
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20Gate = new(false);
        AddressReadWriteBreakpoints memoryReadWriteBreakpoints = new();
        return new Memory(memoryReadWriteBreakpoints, ram, a20Gate);
    }

    /// <summary>
    /// Regression test: the DosProgramSegmentPrefix constructor must not overwrite memory that was
    /// already written by InitializePsp. Before the fix, the constructor unconditionally wrote
    /// CurrentSize = LastFreeSegment (0x9FFF) to memory, corrupting the value that InitializePsp
    /// had just set. This caused TSR programs (like the MAUPITI1 memory manager) to compute the
    /// wrong resident block size — keeping all 40594 paragraphs instead of the correct 6087 —
    /// leaving no room for subsequent programs to load.
    /// </summary>
    [Fact]
    public void Constructor_WhenUsedAsView_DoesNotOverwriteExistingCurrentSize() {
        // Arrange
        Memory memory = CreateMemory();

        // Simulate what InitializePsp does: write a valid CurrentSize to the PSP in memory.
        // PSP segment = 0x016D, block size = 6087 paragraphs → CurrentSize = 0x016D + 6087 = 0x1934
        const ushort pspSegment = 0x016D;
        const ushort blockSize = 6087;
        const ushort expectedCurrentSize = pspSegment + blockSize; // 0x1934
        uint baseAddress = (uint)(pspSegment * 16);
        memory.UInt16[baseAddress + 0x02] = expectedCurrentSize;

        // Act — create a second DosProgramSegmentPrefix view at the same address, exactly as
        // GetCurrentPsp() and the post-InitializePsp lines in LoadAndOrExecuteExe do.
        DosProgramSegmentPrefix pspView = new(memory, baseAddress);

        // Assert — the view constructor must not have overwritten CurrentSize with LastFreeSegment.
        pspView.CurrentSize.Should().Be(expectedCurrentSize,
            "DosProgramSegmentPrefix is a memory view and must not write default values " +
            "over data that was already initialised in memory");
    }

    /// <summary>
    /// The root COMMAND.COM PSP is a special case: its CurrentSize must equal LastFreeSegment.
    /// This value is set explicitly by CreateRootCommandComPsp, not by the constructor.
    /// After the constructor fix, creating a view of the root PSP must still return LastFreeSegment
    /// because CreateRootCommandComPsp wrote it to memory before the view is created.
    /// </summary>
    [Fact]
    public void Constructor_WhenViewingRootPspAfterExplicitInit_ReadsLastFreeSegment() {
        // Arrange
        Memory memory = CreateMemory();

        uint rootBaseAddress = (uint)(DosProcessManager.CommandComSegment * 16);
        // Simulate what CreateRootCommandComPsp writes: CurrentSize = LastFreeSegment.
        memory.UInt16[rootBaseAddress + 0x02] = DosMemoryManager.LastFreeSegment;

        // Act — create a view (as GetCurrentPsp() does inside CreateRootCommandComPsp).
        DosProgramSegmentPrefix rootPspView = new(memory, rootBaseAddress);

        // Assert — we read back what was written; the constructor did not change it.
        rootPspView.CurrentSize.Should().Be(DosMemoryManager.LastFreeSegment,
            "root PSP CurrentSize must equal LastFreeSegment as set by CreateRootCommandComPsp");
    }
}
