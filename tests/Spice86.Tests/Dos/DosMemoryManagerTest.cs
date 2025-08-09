namespace Spice86.Tests.Dos;
using FluentAssertions;

using NSubstitute;

using State = Spice86.Core.Emulator.CPU.State;
using EmulatorBreakpointsManager = Spice86.Core.Emulator.VM.Breakpoint.EmulatorBreakpointsManager;
using PauseHandler = Spice86.Core.Emulator.VM.PauseHandler;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Interfaces;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Verifies that MCBs are allocated, released, modified, and freed correctly by DOS.
/// </summary>
public class DosMemoryManagerTests : IDosPspManager {
    // Dependencies needed to construct DosMemoryManager
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;

    // The instance of the DosMemoryManager class that we're testing
    private readonly DosMemoryManager _memoryManager;

    /// <summary>
    /// Creates the DosMemoryManager instance to test with fake memory for each test case.
    /// </summary>
    public DosMemoryManagerTests() {
        // DosMemoryManager and several of its dependencies need a logger. It's a pretty common base
        // dependency, so create that first.
        _loggerService = Substitute.For<ILoggerService>();

        // Creating the backing memory for the memory manager to allocate requires a fair number of
        // dependencies, unfortunately. Create those now so that we have our nice, shiny chunk of
        // memory to use for these tests.
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        PauseHandler pauseHandler = new(_loggerService);
        State cpuState = new();
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, cpuState);
        A20Gate a20Gate = new(enabled: false);
        _memory = new Memory(emulatorBreakpointsManager.MemoryReadWriteBreakpoints, ram, a20Gate,
            initializeResetVector: true);

        // Arrange
        _memoryManager = new DosMemoryManager(_memory, this, _loggerService);
    }

    /// <summary>
    /// Returns a fixed offset for the PSP for all of our test cases that simulates the default
    /// starting offset of 0x1000 where a real program would typically be loaded.
    /// </summary>
    public ushort GetCurrentPspSegment() => 0xFF0;

    /// <summary>
    /// Ensures that the memory manager with nothing allocated contains one giant MCB that
    /// represents the entirety of accessible conventional memory from the starting segment to the
    /// beginning of the video segment.
    /// </summary>
    [Fact]
    public void FindLargestFreeAtCreation() {
        // Act
        DosMemoryControlBlock block = _memoryManager.FindLargestFree();

        // Assert
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeTrue();
        block.IsLast.Should().BeTrue();
        block.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(36879);
        block.AllocationSizeInBytes.Should().Be(590064);
    }

    /// <summary>
    /// Ensures that the memory manager can allocate the first block of memory after it has been
    /// initialized.
    /// </summary>
    [Fact]
    public void AllocateFirstMemoryBlock() {
        // Act
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(16300);

        // Assert
        block.Should().NotBeNull();
        if (block is null) {
            return;
        }
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeFalse();
        block.IsLast.Should().BeFalse();
        block.PspSegment.Should().Be(GetCurrentPspSegment());
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(16300);
        block.AllocationSizeInBytes.Should().Be(260800);
    }

    /// <summary>
    /// Ensures that the memory manager does not return a memory block if it does not have enough
    /// free memory to allocate.
    /// </summary>
    /// <remarks>
    /// With an initial starting segment of 0xFF0, there are 36879 paragraphs (590064 bytes) before
    /// the first segment of video memory (0xA000). Therefore this test case asks the memory manager
    /// to allocate one additional paragraph beyond the end of its total free memory to ensure that
    /// it doesn't do it. That makes it a boundary test.
    /// </remarks>
    [Fact]
    public void AllocateNotEnoughFreeSpace() {
        // Act
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(36880);

        // Assert
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager can allocate its entire free memory in multiple blocks.
    /// </summary>
    [Fact]
    public void AllocateFullMemoryInMultipleBlocks() {
        // Act
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(16300);
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(20576);
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(1);
        DosMemoryControlBlock? block4 = _memoryManager.AllocateMemoryBlock(1);

        // Assert
        block1.Should().NotBeNull();
        if (block1 is null) {
            return;
        }
        block1.IsValid.Should().BeTrue();
        block1.IsFree.Should().BeFalse();
        block1.IsLast.Should().BeFalse();
        block1.PspSegment.Should().Be(GetCurrentPspSegment());
        block1.DataBlockSegment.Should().Be(0xFF0);
        block1.Size.Should().Be(16300);
        block1.AllocationSizeInBytes.Should().Be(260800);

        block2.Should().NotBeNull();
        if (block2 is null) {
            return;
        }
        block2.IsValid.Should().BeTrue();
        block2.IsFree.Should().BeFalse();
        block2.IsLast.Should().BeFalse();
        block2.PspSegment.Should().Be(GetCurrentPspSegment());
        block2.DataBlockSegment.Should().Be(0x4F9D);
        block2.Size.Should().Be(20576);
        block2.AllocationSizeInBytes.Should().Be(329216);

        block3.Should().NotBeNull();
        if (block3 is null) {
            return;
        }
        block3.IsValid.Should().BeTrue();
        block3.IsFree.Should().BeFalse();
        block3.IsLast.Should().BeTrue();
        block3.PspSegment.Should().Be(GetCurrentPspSegment());
        block3.DataBlockSegment.Should().Be(0x9FFE);
        block3.Size.Should().Be(1);
        block3.AllocationSizeInBytes.Should().Be(16);

        block4.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager cannot allocate a memory block if it has enough free memory,
    /// but it is not contiguous.
    /// </summary>
    [Fact]
    public void AllocateNonContiguousMemory() {
        // Act
        List<DosMemoryControlBlock> allocated = new();
        for (int i = 0; i < 7; i++) {
            DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(5267);
            if (block is not null) {
                allocated.Add(block);
            }
        }
        for (int i = 0; i < allocated.Count; i++) {
            // Release every other memory block to create fragmented memory.
            if ((i % 2) != 0) {
                _memoryManager.FreeMemoryBlock((ushort)(allocated[i].DataBlockSegment - 1));
            }
        }
        DosMemoryControlBlock? largeBlock = _memoryManager.AllocateMemoryBlock(5270);

        // Assert
        allocated.Count.Should().Be(7);
        largeBlock.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager can reduce the size of a free block.
    /// </summary>
    /// <remarks>
    /// Technically trying to reduce the size of a previously free memory block will allocate it as
    /// far as DOS is concerned. This is a bit unusual, but some games seem to do this, so we should
    /// make sure that we actually support it.
    /// </remarks>
    [Fact]
    public void ReduceSizeOfFreeBlock() {
        // Act
        DosMemoryControlBlock block;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 16300, out block);

        // Assert
        errorCode.Should().Be(DosErrorCode.NoError);
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeFalse();
        block.IsLast.Should().BeFalse();
        block.PspSegment.Should().Be(GetCurrentPspSegment());
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(16300);
        block.AllocationSizeInBytes.Should().Be(260800);
    }

    /// <summary>
    /// Ensures that the memory manager cannot extend the size of a free block.
    /// </summary>
    /// <remarks>
    /// A free memory block is always going to consume as much contiguous free space as is
    /// available. Even if memory is fragmented, the memory manager will ensure that it joins
    /// contiguous blocks when they are freed to make sure that we never end up with multiple
    /// conjoining free blocks of memory. Therefore even though a free memory block can effectively
    /// be allocated by trying to reduce its size, it can never be allocated by trying to increase
    /// it because there will never be enough free space to do that. This test case verifies that it
    /// always fails so that we don't end up allocating any overlapping memory.
    /// </remarks>
    [Fact]
    public void ExtendSizeOfFreeBlock() {
        // Act
        DosMemoryControlBlock block;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 36880, out block);

        // Assert
        errorCode.Should().Be(DosErrorCode.InsufficientMemory);
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeTrue();
        block.IsLast.Should().BeTrue();
        block.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(36879);
        block.AllocationSizeInBytes.Should().Be(590064);
    }

    /// <summary>
    /// Ensures that the memory manager cannot extend the size of a memory block with an invalid
    /// address (that wasn't actually allocated, so has no MCB).
    /// </summary>
    [Fact]
    public void ModifySizeOfInvalidBlock() {
        // Act
        DosMemoryControlBlock block;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0x1000, 20, out block);

        // Assert
        errorCode.Should().Be(DosErrorCode.MemoryControlBlockDestroyed);
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeTrue();
        block.IsLast.Should().BeTrue();
        block.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(36879);
        block.AllocationSizeInBytes.Should().Be(590064);
    }

    /// <summary>
    /// Ensures that the memory manager can reduce the size of an allocated block.
    /// </summary>
    [Fact]
    public void ReduceSizeOfAllocatedBlock() {
        // Act
        DosMemoryControlBlock? orignalBlock = _memoryManager.AllocateMemoryBlock(16300);
        DosMemoryControlBlock modifiedBlock;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 20, out modifiedBlock);

        // Assert
        orignalBlock.Should().NotBeNull();
        errorCode.Should().Be(DosErrorCode.NoError);
        modifiedBlock.IsValid.Should().BeTrue();
        modifiedBlock.IsFree.Should().BeFalse();
        modifiedBlock.IsLast.Should().BeFalse();
        modifiedBlock.PspSegment.Should().Be(GetCurrentPspSegment());
        modifiedBlock.DataBlockSegment.Should().Be(0xFF0);
        modifiedBlock.Size.Should().Be(20);
        modifiedBlock.AllocationSizeInBytes.Should().Be(320);
    }

    /// <summary>
    /// Ensures that the memory manager can extend the size of an allocated block if it does not
    /// have any allocated blocks after it.
    /// </summary>
    [Fact]
    public void ExtendSizeOfAllocatedBlock() {
        // Act
        DosMemoryControlBlock? orignalBlock = _memoryManager.AllocateMemoryBlock(9572);
        DosMemoryControlBlock modifiedBlock;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 9815, out modifiedBlock);

        // Assert
        orignalBlock.Should().NotBeNull();
        errorCode.Should().Be(DosErrorCode.NoError);
        modifiedBlock.IsValid.Should().BeTrue();
        modifiedBlock.IsFree.Should().BeFalse();
        modifiedBlock.IsLast.Should().BeFalse();
        modifiedBlock.PspSegment.Should().Be(GetCurrentPspSegment());
        modifiedBlock.DataBlockSegment.Should().Be(0xFF0);
        modifiedBlock.Size.Should().Be(9815);
        modifiedBlock.AllocationSizeInBytes.Should().Be(157040);
    }

    /// <summary>
    /// Ensures that the memory manager succeeds in "changing" the size of an allocated block to its
    /// current size.
    /// </summary>
    /// <remarks>
    /// A program may try to change the size of a memory block that it previously allocated to the
    /// same size. Although this is kind of silly, it may be worth it just to be sure rather than
    /// trying to track the current size of the block itself. Some games seem to do this, so let's
    /// make sure that it works properly and doesn't return an error code.
    /// </remarks>
    [Fact]
    public void ModifySizeOfAllocatedBlockToCurrentSize() {
        // Act
        DosMemoryControlBlock? orignalBlock = _memoryManager.AllocateMemoryBlock(16300);
        DosMemoryControlBlock modifiedBlock;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 16300, out modifiedBlock);

        // Assert
        orignalBlock.Should().NotBeNull();
        errorCode.Should().Be(DosErrorCode.NoError);
        modifiedBlock.IsValid.Should().BeTrue();
        modifiedBlock.IsFree.Should().BeFalse();
        modifiedBlock.IsLast.Should().BeFalse();
        modifiedBlock.PspSegment.Should().Be(GetCurrentPspSegment());
        modifiedBlock.DataBlockSegment.Should().Be(0xFF0);
        modifiedBlock.Size.Should().Be(16300);
        modifiedBlock.AllocationSizeInBytes.Should().Be(260800);
    }

    /// <summary>
    /// Ensures that the memory manager can extend the size of a block that was previously created
    /// by modifying free space.
    /// </summary>
    /// <remarks>
    /// In theory, this test case should be the same as the ModifySizeOfAllocatedBlockToCurrentSize
    /// test case. The only difference is that this one extends the size of a block that was
    /// implicitly allocated by modifying the MCB of what was previously free space (which is what
    /// happens when we load an executable and reserve space for it and its stack in memory), and
    /// the other one modifies the size of a block that was directly allocated. They should have the
    /// same behavior, and they should both modify the size successfully. There are programs that do
    /// both.
    /// </remarks>
    [Fact]
    public void ExtendSizeOfPreviouslyModifiedBlock() {
        // Act
        DosMemoryControlBlock originalBlock;
        DosErrorCode originalErrorCode = _memoryManager.TryModifyBlock(0xFF0, 9572, out originalBlock);
        DosMemoryControlBlock modifiedBlock;
        DosErrorCode modifiedErrorCode = _memoryManager.TryModifyBlock(0xFF0, 9815, out modifiedBlock);

        // Assert
        originalErrorCode.Should().Be(DosErrorCode.NoError);
        modifiedErrorCode.Should().Be(DosErrorCode.NoError);
        modifiedBlock.IsValid.Should().BeTrue();
        modifiedBlock.IsFree.Should().BeFalse();
        modifiedBlock.IsLast.Should().BeFalse();
        modifiedBlock.PspSegment.Should().Be(GetCurrentPspSegment());
        modifiedBlock.DataBlockSegment.Should().Be(0xFF0);
        modifiedBlock.Size.Should().Be(9815);
        modifiedBlock.AllocationSizeInBytes.Should().Be(157040);
    }

    /// <summary>
    /// Ensures that the memory manager cannot extend the size of an allocated block if it has
    /// another allocated block immediately after it.
    /// </summary>
    [Fact]
    public void ExtendSizeOfAllocatedBlockWithAnotherBlockFollowing() {
        // Act
        DosMemoryControlBlock? orignalBlock = _memoryManager.AllocateMemoryBlock(16300);
        DosMemoryControlBlock? secondBlock = _memoryManager.AllocateMemoryBlock(300);
        DosMemoryControlBlock modifiedBlock;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 16400, out modifiedBlock);

        // Assert
        orignalBlock.Should().NotBeNull();
        secondBlock.Should().NotBeNull();
        errorCode.Should().Be(DosErrorCode.InsufficientMemory);
        modifiedBlock.IsValid.Should().BeTrue();
        modifiedBlock.IsFree.Should().BeTrue();
        modifiedBlock.IsLast.Should().BeTrue();
        modifiedBlock.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        modifiedBlock.DataBlockSegment.Should().Be(0x50CA);
        modifiedBlock.Size.Should().Be(20277);
        modifiedBlock.AllocationSizeInBytes.Should().Be(324432);
    }

    /// <summary>
    /// Ensures that the memory manager cannot extend the size of an allocated block if it has free
    /// space after it, but not as much as requested.
    /// </summary>
    [Fact]
    public void ExtendSizeOfAllocatedBlockWithoutEnoughSpace() {
        // Act
        DosMemoryControlBlock? orignalBlock = _memoryManager.AllocateMemoryBlock(16300);
        DosMemoryControlBlock? secondBlock = _memoryManager.AllocateMemoryBlock(100);
        DosMemoryControlBlock? thirdBlock = _memoryManager.AllocateMemoryBlock(300);
        bool isSecondBlockFreed = false;
        if (secondBlock is not null) {
            isSecondBlockFreed = _memoryManager.FreeMemoryBlock((ushort)(secondBlock.DataBlockSegment - 1));
        }
        DosMemoryControlBlock modifiedBlock;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 16500, out modifiedBlock);

        // Assert
        orignalBlock.Should().NotBeNull();
        isSecondBlockFreed.Should().BeTrue();
        thirdBlock.Should().NotBeNull();
        errorCode.Should().Be(DosErrorCode.InsufficientMemory);
        modifiedBlock.IsValid.Should().BeTrue();
        modifiedBlock.IsFree.Should().BeTrue();
        modifiedBlock.IsLast.Should().BeTrue();
        modifiedBlock.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        modifiedBlock.DataBlockSegment.Should().Be(0x512F);
        modifiedBlock.Size.Should().Be(20176);
        modifiedBlock.AllocationSizeInBytes.Should().Be(322816);
    }

    /// <summary>
    /// Ensures that the memory manager can release an allocated memory block.
    /// </summary>
    [Fact]
    public void ReleaseAllocatedMemoryBlock() {
        // Act
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(16300);
        bool isBlockFreed = _memoryManager.FreeMemoryBlock(0xFEF);

        // Assert
        block.Should().NotBeNull();
        isBlockFreed.Should().BeTrue();
        if (block is null) {
            return;
        }
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeTrue();
        block.IsLast.Should().BeTrue();
        block.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(36879);
        block.AllocationSizeInBytes.Should().Be(590064);
    }

    /// <summary>
    /// Ensures that the memory manager returns no error if it is asked to release a free block.
    /// </summary>
    /// <remarks>
    /// There's a reasonable argument to be made that the memory manager should return an error if
    /// it is asked to free a memory block that is already free. However, that is not what the
    /// official MS-DOS 4.0 arena allocator does. It has no additional check for whether it's
    /// already free, and it says that it successfully freed it. This test case ensures that we are
    /// consistent with MS-DOS's behavior. <br/>
    /// https://github.com/microsoft/MS-DOS/blob/main/v4.0/src/DOS/ALLOC.ASM#L334-L361
    /// </remarks>
    [Fact]
    public void ReleaseFreeMemoryBlock() {
        // Act
        bool isBlockFreed = _memoryManager.FreeMemoryBlock(0xFEF);

        // Assert
        isBlockFreed.Should().BeTrue();
    }

    /// <summary>
    /// Ensures that the memory manager returns an error if it is asked to release a memory block
    /// with an invalid address (that wasn't actually allocated, so has no MCB).
    /// </summary>
    [Fact]
    public void ReleaseInvalidMemoryBlock() {
        // Act
        bool isBlockFreed = _memoryManager.FreeMemoryBlock(0x1234);

        // Assert
        isBlockFreed.Should().BeFalse();
    }

    /// <summary>
    /// Ensures that the memory manager allocates a block of memory for the program loaded at the
    /// data segment and stack address in the given CPU state.
    /// </summary>
    [Fact]
    public void ReserveSpaceForExeAndStack() {
        // Arrange
        State cpuState = new();
        cpuState.SS = 0x4F3C;
        cpuState.SP = 0x600;
        cpuState.DS = GetCurrentPspSegment();
        cpuState.ES = GetCurrentPspSegment();

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExeAndStack(cpuState);

        // Assert
        block.Should().NotBeNull();
        if (block is null) {
            return;
        }
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeFalse();
        block.IsLast.Should().BeFalse();
        block.PspSegment.Should().Be(GetCurrentPspSegment());
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(16301);
        block.AllocationSizeInBytes.Should().Be(260816);
    }

    /// <summary>
    /// Ensures that the memory manager rounds up to the nearest paragraph when it allocates a block
    /// of memory for the program loaded at the data segment and stack address in the given CPU
    /// state.
    /// </summary>
    /// <remarks>
    /// It may not be possible to hit this case outside of a contrived unit test case like this. A
    /// well-behaved DOS executable should always align to a paragraph boundary when it is loaded.
    /// However, just in case it doesn't, this test case verifies that DosMemoryManager accounts for
    /// it.
    /// </remarks>
    [Fact]
    public void ReserveSpaceForExeAndStackWithNonParagraphBoundary() {
        // Arrange
        State cpuState = new();
        cpuState.SS = 0x4F3C;
        cpuState.SP = 0x601; // This is one byte different than the initial test case.
        cpuState.DS = GetCurrentPspSegment();
        cpuState.ES = GetCurrentPspSegment();

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExeAndStack(cpuState);

        // Assert
        block.Should().NotBeNull();
        if (block is null) {
            return;
        }
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeFalse();
        block.IsLast.Should().BeFalse();
        block.PspSegment.Should().Be(GetCurrentPspSegment());
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(16302);
        block.AllocationSizeInBytes.Should().Be(260832);
    }

    /// <summary>
    /// Ensures that the memory manager does not try to allocate a block of memory after a COM file
    /// was loaded because the CPU is not in the correct state.
    /// </summary>
    /// <remarks>
    /// We may eventually want to add support for reserving space for COM files as well. This test
    /// case ensures that we don't do anything bad with them right now (or at least no worse than
    /// before we started reserving space for any programs), but it doesn't necessarily represent
    /// the intended final state. It will likely need to change at some point in the future when we
    /// figure out what we need to do to properly reserve space for COM files too.
    /// </remarks>
    [Fact]
    public void ReserveSpaceForComFile() {
        // Arrange
        State cpuState = new();
        cpuState.SS = 0;
        cpuState.SP = 0;
        cpuState.DS = GetCurrentPspSegment();
        cpuState.ES = GetCurrentPspSegment();

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExeAndStack(cpuState);

        // Assert
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager does not try to allocate a block of memory if the stack
    /// segment and data segment can't realistically point to the right place.
    /// </summary>
    /// <remarks>
    /// This test case is just testing an additional protection in ReserveSpaceForExeAndStack() that
    /// should, in theory, never be hit when running a real program as long as it was called
    /// immediately after the program was loaded and the CPU state was setup correctly, as
    /// documented. Or we have a bug. Either way, this test case ensures that our handling for this
    /// unlikely edge case works correctly.
    /// </remarks>
    [Fact]
    public void ReserveSpaceWithCpuInInvalidState() {
        // Arrange
        State cpuState = new();
        cpuState.SS = 0x4F3C;
        cpuState.SP = 0x600;
        cpuState.DS = 0x7600;
        cpuState.ES = 0x7600;

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExeAndStack(cpuState);

        // Assert
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager returns null if the program was loaded at an invalid base
    /// address that is not the start of a valid block.
    /// </summary>
    [Fact]
    public void ReserveSpaceWithInvalidExeStartSegment() {
        // Arrange
        State cpuState = new();
        cpuState.SS = 0x4F3C;
        cpuState.SP = 0x600;
        cpuState.DS = (ushort)(GetCurrentPspSegment() + 1);
        cpuState.ES = cpuState.DS;

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExeAndStack(cpuState);

        // Assert
        block.Should().BeNull();
    }
}