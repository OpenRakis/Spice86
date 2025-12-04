namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

using Configuration = Spice86.Core.CLI.Configuration;

/// <summary>
/// Tests for MCB joining behavior that matches FreeDOS kernel implementation.
/// These tests validate the critical behavior of joining adjacent free MCBs,
/// which is essential for DOS memory management compatibility.
/// This includes the FreeDOS-compatible fix for marking unlinked MCBs as "fake"
/// (size = 0xFFFF), which was causing crashes in programs like Doom 8088 that
/// may manually walk the MCB chain or perform double-free operations.
/// </summary>
public class DosMemoryManagerMcbJoinTests {
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;

    public DosMemoryManagerMcbJoinTests() {
        _loggerService = Substitute.For<ILoggerService>();
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        _memory = new Memory(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);

        Configuration configuration = new Configuration {
            ProgramEntryPointSegment = (ushort)0x1000
        };
        DosSwappableDataArea dosSwappableDataArea = new(_memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        _pspTracker = new(configuration, _memory, dosSwappableDataArea, _loggerService);

        _memoryManager = new DosMemoryManager(_memory, _pspTracker, _loggerService);
    }

    /// <summary>
    /// Tests that freeing a block and then allocating triggers proper MCB joining.
    /// This validates the FreeDOS-style behavior where adjacent free blocks are coalesced.
    /// </summary>
    [Fact]
    public void FreeAndReallocate_ShouldJoinAdjacentFreeBlocks() {
        // Arrange: Allocate two small blocks
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(100);
        
        block1.Should().NotBeNull();
        block2.Should().NotBeNull();
        
        ushort block1Segment = block1!.DataBlockSegment;
        ushort block2Segment = block2!.DataBlockSegment;

        // Act: Free the first block
        bool freed = _memoryManager.FreeMemoryBlock((ushort)(block1Segment - 1));
        freed.Should().BeTrue();

        // The next allocation should find the free block
        // This tests that the MCB chain remains valid after freeing
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(50);
        
        // Assert: Should successfully allocate from the freed space
        block3.Should().NotBeNull();
        block3!.DataBlockSegment.Should().Be(block1Segment);
        block3.Size.Should().Be(50);
    }

    /// <summary>
    /// Tests that the MCB chain maintains validity after multiple allocations and frees.
    /// This is critical for programs that do complex memory management like Doom.
    /// </summary>
    [Fact]
    public void ComplexAllocationPattern_ShouldMaintainValidMcbChain() {
        // Arrange: Create a pattern of allocated and freed blocks
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(200);
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(300);
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(150);
        
        block1.Should().NotBeNull();
        block2.Should().NotBeNull();
        block3.Should().NotBeNull();

        // Act: Free middle block
        bool freed2 = _memoryManager.FreeMemoryBlock((ushort)(block2!.DataBlockSegment - 1));
        freed2.Should().BeTrue();

        // Check MCB chain integrity
        bool chainValid = _memoryManager.CheckMcbChain();
        chainValid.Should().BeTrue();

        // Free first block - this should join with the freed middle block
        bool freed1 = _memoryManager.FreeMemoryBlock((ushort)(block1!.DataBlockSegment - 1));
        freed1.Should().BeTrue();

        // Check MCB chain is still valid after joining
        chainValid = _memoryManager.CheckMcbChain();
        chainValid.Should().BeTrue();

        // The joined blocks should now be available as one large block
        DosMemoryControlBlock largest = _memoryManager.FindLargestFree();
        largest.Should().NotBeNull();
        // After joining block1 (200) + MCB (1) + block2 (300), we should have at least 500 paragraphs
        // in the largest free block (could be more if it joined with following free space)
        largest.Size.Should().BeGreaterThanOrEqualTo(500);
    }

    /// <summary>
    /// Tests that allocating all free memory and then freeing it results in a valid single MCB.
    /// This simulates what happens during program initialization and cleanup.
    /// </summary>
    [Fact]
    public void AllocateAllAndFreeAll_ShouldRestoreToSingleFreeBlock() {
        // Arrange: Find initial state
        DosMemoryControlBlock initialBlock = _memoryManager.FindLargestFree();
        ushort initialSize = initialBlock.Size;

        // Act: Allocate everything
        DosMemoryControlBlock? allocated = _memoryManager.AllocateMemoryBlock(initialSize);
        allocated.Should().NotBeNull();

        // Free it
        bool freed = _memoryManager.FreeMemoryBlock((ushort)(allocated!.DataBlockSegment - 1));
        freed.Should().BeTrue();

        // Assert: Should be back to single large free block
        DosMemoryControlBlock finalBlock = _memoryManager.FindLargestFree();
        finalBlock.Size.Should().Be(initialSize);
        finalBlock.IsFree.Should().BeTrue();
        finalBlock.IsLast.Should().BeTrue();
    }

    /// <summary>
    /// Tests that freeing blocks in reverse order properly joins them.
    /// This is a common pattern in programs that allocate multiple blocks and then cleanup.
    /// </summary>
    [Fact]
    public void FreeBlocksInReverseOrder_ShouldJoinCorrectly() {
        // Arrange
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(100);
        
        block1.Should().NotBeNull();
        block2.Should().NotBeNull();
        block3.Should().NotBeNull();

        // Act: Free in reverse order
        _memoryManager.FreeMemoryBlock((ushort)(block3!.DataBlockSegment - 1));
        _memoryManager.FreeMemoryBlock((ushort)(block2!.DataBlockSegment - 1));
        _memoryManager.FreeMemoryBlock((ushort)(block1!.DataBlockSegment - 1));

        // Assert: MCB chain should be valid
        bool chainValid = _memoryManager.CheckMcbChain();
        chainValid.Should().BeTrue();

        // Should be able to allocate a large block that spans all three freed blocks
        DosMemoryControlBlock? largeBlock = _memoryManager.AllocateMemoryBlock(302); // 3 * 100 + 2 MCBs
        largeBlock.Should().NotBeNull();
    }

    /// <summary>
    /// Tests the scenario where adjacent free blocks exist and should be joined during allocation.
    /// This tests the FindCandidatesForAllocation path which calls JoinBlocks.
    /// </summary>
    [Fact]
    public void AllocateAfterCreatingFragmentation_ShouldJoinFreeBlocks() {
        // Arrange: Create fragmentation
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? block4 = _memoryManager.AllocateMemoryBlock(100);
        
        // Free alternating blocks to create fragmentation
        _memoryManager.FreeMemoryBlock((ushort)(block1!.DataBlockSegment - 1));
        _memoryManager.FreeMemoryBlock((ushort)(block3!.DataBlockSegment - 1));

        // Act: Allocate a block that fits in one of the freed blocks
        DosMemoryControlBlock? newBlock = _memoryManager.AllocateMemoryBlock(50);
        
        // Assert
        newBlock.Should().NotBeNull();
        newBlock!.Size.Should().Be(50);
        
        // MCB chain should still be valid
        bool chainValid = _memoryManager.CheckMcbChain();
        chainValid.Should().BeTrue();
    }

    /// <summary>
    /// Tests that after joining MCBs, the unlinked MCB is marked as "fake" (size = 0xFFFF).
    /// This matches FreeDOS kernel behavior and is critical for compatibility with programs
    /// that may manually walk the MCB chain or perform double-free operations.
    /// </summary>
    [Fact]
    public void JoinBlocks_ShouldMarkUnlinkedMcbAsFake() {
        // Arrange: Allocate two adjacent blocks
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(100);
        
        block1.Should().NotBeNull();
        block2.Should().NotBeNull();
        
        // Get the segment of what will be the unlinked MCB after joining
        ushort unlinkedMcbSegment = (ushort)(block1!.DataBlockSegment + block1.Size);
        
        // Free both blocks
        _memoryManager.FreeMemoryBlock((ushort)(block1.DataBlockSegment - 1));
        _memoryManager.FreeMemoryBlock((ushort)(block2!.DataBlockSegment - 1));
        
        // Act: Allocate again to trigger joining
        DosMemoryControlBlock? newBlock = _memoryManager.AllocateMemoryBlock(150);
        
        // Assert: The unlinked MCB should have been marked as "fake" with size 0xFFFF
        // This is what FreeDOS does to prevent issues with programs that might accidentally
        // reference the now-invalid MCB
        DosMemoryControlBlock unlinkedMcb = new DosMemoryControlBlock(_memory, 
            MemoryUtils.ToPhysicalAddress(unlinkedMcbSegment, 0));
        
        // The unlinked MCB should have size 0xFFFF to mark it as invalid/fake
        // This prevents mcbValid() from returning true for this unlinked block
        unlinkedMcb.Size.Should().Be(0xFFFF, 
            "FreeDOS marks unlinked MCBs with size 0xFFFF to indicate they are 'fake' and should be ignored");
    }
}
