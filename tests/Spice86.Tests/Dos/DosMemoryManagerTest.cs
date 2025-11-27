namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

using Configuration = Spice86.Core.CLI.Configuration;
using EmulatorBreakpointsManager = Spice86.Core.Emulator.VM.Breakpoint.EmulatorBreakpointsManager;
using PauseHandler = Spice86.Core.Emulator.VM.PauseHandler;
using State = Spice86.Core.Emulator.CPU.State;

/// <summary>
/// Verifies that MCBs are allocated, released, modified, and freed correctly by DOS.
/// </summary>
public class DosMemoryManagerTests {
    // Dependencies needed to construct DosMemoryManager
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly DosProgramSegmentPrefixTracker _pspTracker;

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
        State cpuState = new(CpuModel.INTEL_80286);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, cpuState);
        A20Gate a20Gate = new(enabled: false);
        _memory = new Memory(emulatorBreakpointsManager.MemoryReadWriteBreakpoints, ram, a20Gate,
            initializeResetVector: true);

        // Create the PSP tracker that reads the configuration and informs the memory manager what
        // it can allocate. We can effectively start it wherever we want for testing.
        var configuration = new Configuration {
            // 0x1000 is the default program entry point segment for Spice86 at the time these unit
            // tests were written. It makes the math easier. However it's wasting some of lower
            // memory that's completely unused, and will never be allocated. That's technically
            // okay; it just reduces the amount of useable convention memory compared to real DOS.
            //
            // Even if the default changes in Spice86 later, we probably shouldn't change it in
            // these unit tests because most of them rely on the amount of available space that the
            // memory manager has to allocate based on this starting point. If we changed it, all of
            // those values used in these tests would have to be recomputed. There's really no
            // reason to do that. The memory manager works the same way regardless of the starting
            // point, so these unit tests are valid regardless.
            ProgramEntryPointSegment = (ushort)0x1000
        };
        _pspTracker = new(configuration, _memory, _loggerService);

        // Arrange
        _memoryManager = new DosMemoryManager(_memory, _pspTracker, _loggerService);
    }

    /// <summary>
    /// Fills the entire memory block with the given byte.
    /// </summary>
    /// <remarks>
    /// We don't have anything in particular that we really <em>need</em> to do with the blocks that
    /// we allocate in these unit tests, unlike in a real program. We just need to ensure that they
    /// were allocated properly. Part of that validation is making sure that the whole block that
    /// was allocated is <em>really</em> available for the program to use. Since the MCB is one
    /// paragraph before the data segment of each block, if we get it wrong, we'll likely clobber
    /// the MCB when we put data into the block, which it's possible for us to detect. This function
    /// fills the block with the given byte to help detect that condition.
    /// </remarks>
    /// <param name="block">The memory block to fill with data.</param>
    /// <param name="fillByte">The data to fill the block with.</param>
    private void FillMemoryBlock(DosMemoryControlBlock? block, byte fillByte = 0xFF) {
        // Since this is just a test method, and many of the allocator functions return nullable
        // blocks, check for that first. Technically we could make the tests validate the validity
        // of the block before calling this function, but that would be less convenient. We'll catch
        // that later on in the test case anyhow even if we don't fill it.
        if (block is null || !block.IsValid) {
            return;
        }

        uint startAddress = MemoryUtils.ToPhysicalAddress(block.DataBlockSegment, 0);
        int sizeInBytes = block.AllocationSizeInBytes;
        for (int i = 0; i < sizeInBytes; i++) {
            _memory.UInt8[startAddress + i] = fillByte;
        }
    }

    /// <summary>
    /// Creates a mocked EXE file header in memory with the given properties that can be used to
    /// test the memory allocator.
    /// <summary>
    /// <param name="pages">Number of whole/partial pages in the executable.</param>
    /// <param name="minAlloc">Number of extra paragraphs required by the program.</param>
    /// <param name="maxAlloc">Number of extra paragraphs requested by the program.</param>
    /// <returns>Returns a new EXE file header to use for these unit tests.</returns>
    public DosExeFile CreateMockExe(ushort pages, ushort minAlloc, ushort maxAlloc) {
        // The DOS EXE header is 64 bytes, excluding the relocation table if one is present. It is
        // normally followed by the program image, but we don't need that for these unit tests. We
        // just need the header itself.
        byte[] exe = new byte[64];
        // Start with the signature that identifies the DOS EXE format, "MZ".
        exe[0x00] = 0x4D;
        exe[0x01] = 0x5A;
        // Number of whole/partial pages.
        exe[0x04] = (byte)((pages >> 0) & 0xFF);
        exe[0x05] = (byte)((pages >> 8) & 0xFF);
        // Number of paragraphs in the header.
        exe[0x08] = 0x04;
        exe[0x09] = 0x00;
        // Minimum number of extra paragraphs.
        exe[0x0A] = (byte)((minAlloc >> 0) & 0xFF);
        exe[0x0B] = (byte)((minAlloc >> 8) & 0xFF);
        // Maximum number of extra paragraphs.
        exe[0x0C] = (byte)((maxAlloc >> 0) & 0xFF);
        exe[0x0D] = (byte)((maxAlloc >> 8) & 0xFF);

        return new DosExeFile(new ByteArrayReaderWriter(exe));
    }

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
        block.Size.Should().Be(36880);
        block.AllocationSizeInBytes.Should().Be(590080);
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
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeFalse();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(16300);
        block!.AllocationSizeInBytes.Should().Be(260800);
    }

    /// <summary>
    /// Ensures that the memory manager can allocate the full conventional memory space when nothing
    /// has been allocated yet.
    /// </summary>
    [Fact]
    public void AllocateFullConventionalMemorySpace() {
        // Act
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(36880);

        // Assert
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeTrue();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(36880);
        block!.AllocationSizeInBytes.Should().Be(590080);
    }

    /// <summary>
    /// Ensures that the memory manager does not return a memory block if it does not have enough
    /// free memory to allocate.
    /// </summary>
    /// <remarks>
    /// With an initial starting segment of 0xFF0, there are 36880 paragraphs (590080 bytes) before
    /// the first segment of video memory (0xA000). Therefore this test case asks the memory manager
    /// to allocate one additional paragraph beyond the end of its total free memory to ensure that
    /// it doesn't do it. That makes it a boundary test.
    /// </remarks>
    [Fact]
    public void AllocateNotEnoughFreeSpace() {
        // Act
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(36881);

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
        FillMemoryBlock(block1);
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(20577);
        FillMemoryBlock(block2);
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(1);
        FillMemoryBlock(block3);
        DosMemoryControlBlock? block4 = _memoryManager.AllocateMemoryBlock(1);
        FillMemoryBlock(block4);

        // Assert
        block1.Should().NotBeNull();
        block1!.IsValid.Should().BeTrue();
        block1!.IsFree.Should().BeFalse();
        block1!.IsLast.Should().BeFalse();
        block1!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block1!.DataBlockSegment.Should().Be(0xFF0);
        block1!.Size.Should().Be(16300);
        block1!.AllocationSizeInBytes.Should().Be(260800);

        block2.Should().NotBeNull();
        block2!.IsValid.Should().BeTrue();
        block2!.IsFree.Should().BeFalse();
        block2!.IsLast.Should().BeFalse();
        block2!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block2!.DataBlockSegment.Should().Be(0x4F9D);
        block2!.Size.Should().Be(20577);
        block2!.AllocationSizeInBytes.Should().Be(329232);

        block3.Should().NotBeNull();
        block3!.IsValid.Should().BeTrue();
        block3!.IsFree.Should().BeFalse();
        block3!.IsLast.Should().BeTrue();
        block3!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block3!.DataBlockSegment.Should().Be(0x9FFF);
        block3!.Size.Should().Be(1);
        block3!.AllocationSizeInBytes.Should().Be(16);

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
                _memoryManager.FreeMemoryBlock(allocated[i]);
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
        block.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
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
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 36881, out block);

        // Assert
        errorCode.Should().Be(DosErrorCode.InsufficientMemory);
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeTrue();
        block.IsLast.Should().BeTrue();
        block.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(36880);
        block.AllocationSizeInBytes.Should().Be(590080);
    }

    /// <summary>
    /// Ensures that the memory manager can extend the size of a free block to allocate the full
    /// conventional memory space.
    /// </summary>
    /// <remarks>
    /// DOS has no way for programs to directly ask it how much conventional memory is still free.
    /// There are two ways that programs typically do this.
    /// <ul>
    /// <li>
    /// First, they may try to modify the current memory block to 0xFFFF, which is guaranteed to
    /// always be larger than the conventional memory space, and use the size of the largest free
    /// block that DOS gives it when allocation fails to determine how much memory there is
    /// available for it to allocate.
    /// </li>
    /// <li>
    /// Second, they may read the MCB of the current memory block and do the math to determine how
    /// much conventional memory should be available, which is possible when you know your starting
    /// segment and the fact that conventional memory always ends at the first block of video
    /// memory: 0xA000.
    /// </li>
    /// </ul>
    /// </br>
    /// Games that determine their allocation the first way should always work no matter how much
    /// the memory manager tells them it has as long as it gets it right. That is tested in the
    /// <c>GetSizeOfStartingConventionalMemory</c> test case.
    /// </br>
    /// Games that determine their allocation the second way, using their own math, will only work
    /// if the memory manager is able to allocate the full DOS conventional memory space - no more,
    /// no less. This test case verifies that boundary.
    /// </remarks>
    [Fact]
    public void ExtendToAllocateFullConventionalMemory() {
        // Act
        DosMemoryControlBlock block;
        DosErrorCode errorCode = _memoryManager.TryModifyBlock(0xFF0, 36880, out block);

        // Assert
        errorCode.Should().Be(DosErrorCode.NoError);
        block.IsValid.Should().BeTrue();
        block.IsFree.Should().BeFalse();
        block.IsLast.Should().BeTrue();
        block.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block.DataBlockSegment.Should().Be(0xFF0);
        block.Size.Should().Be(36880);
        block.AllocationSizeInBytes.Should().Be(590080);
    }

    /// <summary>
    /// Uses the same trick that many DOS games do of asking the memory manager to try to modify the
    /// memory block where the program is loaded to 65535 (0xFFFF), which there will <em>never</em>
    /// be enough conventional memory available to satisfy, to get the actual amount of conventional
    /// memory available so that it can allocate it.
    /// </summary>
    [Fact]
    public void GetSizeOfStartingConventionalMemory() {
        // Act
        DosMemoryControlBlock block1;
        DosMemoryControlBlock block2;
        // Simulate allocating a block for the program image first.
        DosErrorCode errorCode1 = _memoryManager.TryModifyBlock(0xFF0, 1234, out block1);
        // Get the remaining free space.
        DosErrorCode errorCode2 = _memoryManager.TryModifyBlock(0xFF0, 0xFFFF, out block2);

        // Assert
        errorCode1.Should().Be(DosErrorCode.NoError);
        errorCode2.Should().Be(DosErrorCode.InsufficientMemory);

        block1.IsValid.Should().BeTrue();
        block1.IsFree.Should().BeFalse();
        block1.IsLast.Should().BeFalse();
        block1.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block1.DataBlockSegment.Should().Be(0xFF0);
        block1.Size.Should().Be(1234);
        block1.AllocationSizeInBytes.Should().Be(19744);

        block2.IsValid.Should().BeTrue();
        block2.IsFree.Should().BeTrue();
        block2.IsLast.Should().BeTrue();
        block2.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        block2.DataBlockSegment.Should().Be(0x14C3);
        block2.Size.Should().Be(35645);
        block2.AllocationSizeInBytes.Should().Be(570320);
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
        block.Size.Should().Be(36880);
        block.AllocationSizeInBytes.Should().Be(590080);
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
        modifiedBlock.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
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
        modifiedBlock.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
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
        modifiedBlock.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
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
        if (originalErrorCode == DosErrorCode.NoError) {
            FillMemoryBlock(originalBlock);
        }
        DosMemoryControlBlock modifiedBlock;
        DosErrorCode modifiedErrorCode = _memoryManager.TryModifyBlock(0xFF0, 9815, out modifiedBlock);
        if (modifiedErrorCode == DosErrorCode.NoError) {
            FillMemoryBlock(modifiedBlock);
        }

        // Assert
        originalErrorCode.Should().Be(DosErrorCode.NoError);
        modifiedErrorCode.Should().Be(DosErrorCode.NoError);
        modifiedBlock.IsValid.Should().BeTrue();
        modifiedBlock.IsFree.Should().BeFalse();
        modifiedBlock.IsLast.Should().BeFalse();
        modifiedBlock.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
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
        FillMemoryBlock(orignalBlock);
        DosMemoryControlBlock? secondBlock = _memoryManager.AllocateMemoryBlock(300);
        FillMemoryBlock(secondBlock);
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
        modifiedBlock.Size.Should().Be(20278);
        modifiedBlock.AllocationSizeInBytes.Should().Be(324448);
    }

    /// <summary>
    /// Ensures that the memory manager cannot extend the size of an allocated block if it has free
    /// space after it, but not as much as requested.
    /// </summary>
    [Fact]
    public void ExtendSizeOfAllocatedBlockWithoutEnoughSpace() {
        // Act
        DosMemoryControlBlock? orignalBlock = _memoryManager.AllocateMemoryBlock(16300);
        FillMemoryBlock(orignalBlock);
        DosMemoryControlBlock? secondBlock = _memoryManager.AllocateMemoryBlock(100);
        FillMemoryBlock(secondBlock);
        DosMemoryControlBlock? thirdBlock = _memoryManager.AllocateMemoryBlock(300);
        FillMemoryBlock(thirdBlock);
        bool isSecondBlockFreed = false;
        if (secondBlock is not null) {
            isSecondBlockFreed = _memoryManager.FreeMemoryBlock(secondBlock);
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
        modifiedBlock.Size.Should().Be(20177);
        modifiedBlock.AllocationSizeInBytes.Should().Be(322832);
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
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeTrue();
        block!.IsLast.Should().BeTrue();
        block!.PspSegment.Should().Be(DosMemoryControlBlock.FreeMcbMarker);
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(36880);
        block!.AllocationSizeInBytes.Should().Be(590080);
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
    /// Ensures that the memory manager allocates the given block of memory for an EXE.
    /// </summary>
    [Fact]
    public void ReserveSpecificMemoryBlockForExe() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 0, maxAlloc: 65);

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, 0xFF0);

        // Assert
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeFalse();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(16301);
        block!.AllocationSizeInBytes.Should().Be(260816);
    }

    /// <summary>
    /// Ensures that the memory manager allocates a new block of memory for an EXE.
    /// </summary>
    [Fact]
    public void ReserveNewMemoryBlockForExe() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 0, maxAlloc: 65);

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);

        // Assert
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeFalse();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(16301);
        block!.AllocationSizeInBytes.Should().Be(260816);
    }

    /// <summary>
    /// Ensures that the memory manager allocates the given block of memory with the minimum number
    /// of paragraphs that the EXE requires if there is not enough space after it to support the
    /// maximum requested allocation, even if there is a large enough block to support the maximum
    /// allocation at a different address than the one specified.
    /// </summary>
    [Fact]
    public void ReserveSpecificMinSizeMemoryBlockForExe() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 382, minAlloc: 10, maxAlloc: 65);

        // Act
        DosMemoryControlBlock? otherBlock1 = _memoryManager.AllocateMemoryBlock(12292);
        DosMemoryControlBlock? otherBlock2 = _memoryManager.AllocateMemoryBlock(6146);
        bool isOtherBlock1Freed = false;
        if (otherBlock1 is not null) {
            isOtherBlock1Freed = _memoryManager.FreeMemoryBlock(otherBlock1);
        }
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, 0xFF0);

        // Assert
        isOtherBlock1Freed.Should().BeTrue();
        otherBlock2.Should().NotBeNull();
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeFalse();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(12292);
        block!.AllocationSizeInBytes.Should().Be(196672);
    }

    /// <summary>
    /// Ensures that the memory manager allocates a new block of memory with the minimum number of
    /// paragraphs that the EXE requires if there is not a large enough block remaining to support
    /// the maximum requested allocation.
    /// </summary>
    [Fact]
    public void ReserveNewMinSizeMemoryBlockForExe() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 382, minAlloc: 10, maxAlloc: 65);

        // Act
        DosMemoryControlBlock? otherBlock1 = _memoryManager.AllocateMemoryBlock(12292);
        DosMemoryControlBlock? otherBlock2 = _memoryManager.AllocateMemoryBlock(12292);
        DosMemoryControlBlock? otherBlock3 = _memoryManager.AllocateMemoryBlock(6146);
        bool isOtherBlock2Freed = false;
        if (otherBlock2 is not null) {
            isOtherBlock2Freed = _memoryManager.FreeMemoryBlock(otherBlock2);
        }
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);

        // Assert
        otherBlock1.Should().NotBeNull();
        isOtherBlock2Freed.Should().BeTrue();
        otherBlock3.Should().NotBeNull();
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeFalse();
        block!.PspSegment.Should().Be(0x3FF5);
        block!.DataBlockSegment.Should().Be(0x3FF5);
        block!.Size.Should().Be(12292);
        block!.AllocationSizeInBytes.Should().Be(196672);
    }

    /// <summary>
    /// Ensures that the memory manager allocates the maximum amount of memory available at the
    /// given segment address for the EXE if it has no minimum/maximum extra allocation requested.
    /// </summary>
    [Fact]
    public void ReserveSpecificMemoryBlockForExeWithNoExtraAlloc() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 0, maxAlloc: 0);

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, 0xFF0);

        // Assert
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeTrue();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(36880);
        block!.AllocationSizeInBytes.Should().Be(590080);
    }

    /// <summary>
    /// Ensures that the memory manager allocates the largest free block of memory for the EXE if it
    /// has no minimum/maximum extra allocation requested.
    /// </summary>
    [Fact]
    public void ReserveNewMemoryBlockForExeWithNoExtraAlloc() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 0, maxAlloc: 0);

        // Act
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);

        // Assert
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeTrue();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(36880);
        block!.AllocationSizeInBytes.Should().Be(590080);
    }

    /// <summary>
    /// Ensures that the memory manager allocates the maximum amount of memory available at the
    /// given segment address for the EXE if it has no minimum/maximum extra allocation requested
    /// and doesn't have the full memory space available to it.
    /// </summary>
    [Fact]
    public void ReserveLargestMemoryBlockForExeWithNoExtraAllocAtSpecificAddress() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 382, minAlloc: 0, maxAlloc: 0);

        // Act
        DosMemoryControlBlock? otherBlock1 = _memoryManager.AllocateMemoryBlock(12292);
        DosMemoryControlBlock? otherBlock2 = _memoryManager.AllocateMemoryBlock(6146);
        bool isOtherBlock1Freed = false;
        if (otherBlock1 is not null) {
            isOtherBlock1Freed = _memoryManager.FreeMemoryBlock(otherBlock1);
        }
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, 0xFF0);

        // Assert
        isOtherBlock1Freed.Should().BeTrue();
        otherBlock2.Should().NotBeNull();
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeFalse();
        block!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block!.DataBlockSegment.Should().Be(0xFF0);
        block!.Size.Should().Be(12292);
        block!.AllocationSizeInBytes.Should().Be(196672);
    }

    /// <summary>
    /// Ensures that the memory manager allocates the maximum amount of memory available for the EXE
    /// if it has no minimum/maximum extra allocation requested and doesn't have the full memory
    /// space available to it.
    /// </summary>
    [Fact]
    public void ReserveLargestMemoryBlockForExeWithNoExtraAlloc() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 382, minAlloc: 0, maxAlloc: 0);

        // Act
        DosMemoryControlBlock? otherBlock1 = _memoryManager.AllocateMemoryBlock(12292);
        DosMemoryControlBlock? otherBlock2 = _memoryManager.AllocateMemoryBlock(6146);
        bool isOtherBlock1Freed = false;
        if (otherBlock1 is not null) {
            isOtherBlock1Freed = _memoryManager.FreeMemoryBlock(otherBlock1);
        }
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);

        // Assert
        isOtherBlock1Freed.Should().BeTrue();
        otherBlock2.Should().NotBeNull();
        block.Should().NotBeNull();
        block!.IsValid.Should().BeTrue();
        block!.IsFree.Should().BeFalse();
        block!.IsLast.Should().BeTrue();
        block!.PspSegment.Should().Be(0x57F8);
        block!.DataBlockSegment.Should().Be(0x57F8);
        block!.Size.Should().Be(18440);
        block!.AllocationSizeInBytes.Should().Be(295040);
    }

    /// <summary>
    /// Ensures that the memory manager allocates a new block of memory for each EXE when more than
    /// one is loaded.
    /// </summary>
    [Fact]
    public void ReserveNewMemoryBlockForMultipleExes() {
        // Arrange
        DosExeFile exeFile1 = CreateMockExe(pages: 382, minAlloc: 10, maxAlloc: 65);
        DosExeFile exeFile2 = CreateMockExe(pages: 500, minAlloc: 0, maxAlloc: 100);

        // Act
        DosMemoryControlBlock? block1 = _memoryManager.ReserveSpaceForExe(exeFile1);
        if (block1 is not null) {
            _pspTracker.PushPspSegment(block1.PspSegment);
        }
        DosMemoryControlBlock? block2 = _memoryManager.ReserveSpaceForExe(exeFile2);
        if (block2 is not null) {
            _pspTracker.PushPspSegment(block2.PspSegment);
        }

        // Assert
        block1.Should().NotBeNull();
        block2.Should().NotBeNull();

        block1!.IsValid.Should().BeTrue();
        block1!.IsFree.Should().BeFalse();
        block1!.IsLast.Should().BeFalse();
        block1!.PspSegment.Should().Be(_pspTracker.InitialPspSegment);
        block1!.DataBlockSegment.Should().Be(0xFF0);
        block1!.Size.Should().Be(12301);
        block1!.AllocationSizeInBytes.Should().Be(196816);

        block2!.IsValid.Should().BeTrue();
        block2!.IsFree.Should().BeFalse();
        block2!.IsLast.Should().BeFalse();
        block2!.PspSegment.Should().Be(_pspTracker.GetCurrentPspSegment());
        block2!.DataBlockSegment.Should().Be(0x3FFE);
        block2!.Size.Should().Be(16112);
        block2!.AllocationSizeInBytes.Should().Be(257792);
    }

    /// <summary>
    /// Ensures that the memory manager fails to allocate a specific memory block for the EXE if the
    /// minimum number of required paragraphs can't be satisfied for that block.
    /// </summary>
    [Fact]
    public void DoNotReserveSpecificMemoryBlockForExeWithoutMinSpace() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 99, maxAlloc: 205);

        // Act
        DosMemoryControlBlock? otherBlock1 = _memoryManager.AllocateMemoryBlock(16334);
        DosMemoryControlBlock? otherBlock2 = _memoryManager.AllocateMemoryBlock(12292);
        bool isOtherBlock1Freed = false;
        if (otherBlock1 is not null) {
            isOtherBlock1Freed = _memoryManager.FreeMemoryBlock(otherBlock1);
        }
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, 0xFF0);

        // Assert
        isOtherBlock1Freed.Should().BeTrue();
        otherBlock2.Should().NotBeNull();
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager fails to allocate a new memory block for the EXE if the
    /// minimum number of required paragraphs can't be satisfied by any free memory block.
    /// </summary>
    [Fact]
    public void DoNotReserveNewMemoryBlockForExeWithoutMinSpace() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 99, maxAlloc: 205);

        // Act
        DosMemoryControlBlock? initialBlock = _memoryManager.AllocateMemoryBlock(20545);
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);

        // Assert
        initialBlock.Should().NotBeNull();
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager fails to allocate a memory block for the EXE if it has no
    /// minimum/maximum extra allocation requested and the block at the given segment address isn't
    /// large enough to hold the PSP and program image.
    /// </summary>
    [Fact]
    public void DoNotReserveSpecificMemoryBlockForExeWithNoExtraAlloc() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 0, maxAlloc: 0);

        // Act
        DosMemoryControlBlock? otherBlock1 = _memoryManager.AllocateMemoryBlock(6146);
        DosMemoryControlBlock? otherBlock2 = _memoryManager.AllocateMemoryBlock(12292);
        bool isOtherBlock1Freed = false;
        if (otherBlock1 is not null) {
            isOtherBlock1Freed = _memoryManager.FreeMemoryBlock(otherBlock1);
        }
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, 0xFF0);

        // Assert
        isOtherBlock1Freed.Should().BeTrue();
        otherBlock2.Should().NotBeNull();
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager fails to allocate a new memory block for the EXE if it has
    /// no minimum/maximum extra allocation requested and there isn't a large enough block to hold
    /// the PSP and program image.
    /// </summary>
    [Fact]
    public void DoNotReserveNewMemoryBlockForExeWithNoExtraAlloc() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 0, maxAlloc: 0);

        // Act
        DosMemoryControlBlock? otherBlock1 = _memoryManager.AllocateMemoryBlock(12292);
        DosMemoryControlBlock? otherBlock2 = _memoryManager.AllocateMemoryBlock(16235);
        DosMemoryControlBlock? otherBlock3 = _memoryManager.AllocateMemoryBlock(8349);
        bool isOtherBlock2Freed = false;
        if (otherBlock2 is not null) {
            isOtherBlock2Freed = _memoryManager.FreeMemoryBlock(otherBlock2);
        }
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);

        // Assert
        otherBlock1.Should().NotBeNull();
        isOtherBlock2Freed.Should().BeTrue();
        otherBlock3.Should().NotBeNull();
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager fails to allocate/resize the requested memory block for an
    /// EXE if it has already been allocated.
    /// </summary>
    [Fact]
    public void DoNotReserveAlreadyAllocatedBlockForExe() {
        // Arrange
        DosExeFile exeFile = CreateMockExe(pages: 507, minAlloc: 0, maxAlloc: 0);

        // Act
        DosMemoryControlBlock? existingBlock = _memoryManager.AllocateMemoryBlock(18700);
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, 0xFF0);

        // Assert
        existingBlock.Should().NotBeNull();
        block.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the memory manager uses first fit strategy correctly.
    /// </summary>
    [Fact]
    public void AllocateWithFirstFitStrategy() {
        // Arrange - create some fragmented memory
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(1000);
        _memoryManager.AllocateMemoryBlock(2000);
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(1500);
        _memoryManager.FreeMemoryBlock(block1!);
        _memoryManager.FreeMemoryBlock(block3!);
        
        // Set first fit strategy
        _memoryManager.AllocationStrategy = DosMemoryAllocationStrategy.FirstFit;
        
        // Act - allocate a block that fits in the first free block
        DosMemoryControlBlock? block4 = _memoryManager.AllocateMemoryBlock(500);
        
        // Assert - should allocate in the first free block (where block1 was)
        block4.Should().NotBeNull();
        block4!.DataBlockSegment.Should().Be(block1!.DataBlockSegment);
    }

    /// <summary>
    /// Ensures that the memory manager uses best fit strategy correctly.
    /// </summary>
    [Fact]
    public void AllocateWithBestFitStrategy() {
        // Arrange - create some fragmented memory with different sized holes
        // We need to keep some blocks allocated between holes to prevent coalescing
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(500);  // Will be freed -> small hole
        _memoryManager.AllocateMemoryBlock(1000); // Keep allocated
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(2000); // Will be freed -> large hole
        _memoryManager.AllocateMemoryBlock(1000); // Keep allocated
        
        _memoryManager.FreeMemoryBlock(block1!);  // Creates 500 para hole at start
        _memoryManager.FreeMemoryBlock(block3!);  // Creates 2000 para hole in middle
        
        // Set best fit strategy
        _memoryManager.AllocationStrategy = DosMemoryAllocationStrategy.BestFit;
        
        // Act - allocate a block that fits in the small hole but also fits in the large hole
        DosMemoryControlBlock? blockNew = _memoryManager.AllocateMemoryBlock(400);
        
        // Assert - best fit should choose the smaller hole (500) that's just big enough
        blockNew.Should().NotBeNull();
        blockNew!.DataBlockSegment.Should().Be(block1!.DataBlockSegment);
    }

    /// <summary>
    /// Ensures that the memory manager uses last fit strategy correctly.
    /// </summary>
    [Fact]
    public void AllocateWithLastFitStrategy() {
        // Arrange - create some fragmented memory with holes
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(500);  // Will be freed -> first hole
        _memoryManager.AllocateMemoryBlock(1000); // Keep allocated
        DosMemoryControlBlock? block3 = _memoryManager.AllocateMemoryBlock(500);  // Will be freed -> second hole
        _memoryManager.AllocateMemoryBlock(1000); // Keep allocated
        
        _memoryManager.FreeMemoryBlock(block1!);  // Creates hole at start
        _memoryManager.FreeMemoryBlock(block3!);  // Creates hole in middle
        
        // Set last fit strategy
        _memoryManager.AllocationStrategy = DosMemoryAllocationStrategy.LastFit;
        
        // Act - allocate a block that could fit in either hole
        DosMemoryControlBlock? blockNew = _memoryManager.AllocateMemoryBlock(400);
        
        // Assert - last fit should choose the highest address hole (where block3 was)
        // since there's also free space after block4, the last fit picks the last candidate
        blockNew.Should().NotBeNull();
        // The allocation should be in one of the later candidates (higher address)
        blockNew!.DataBlockSegment.Should().BeGreaterThan(block1!.DataBlockSegment);
    }

    /// <summary>
    /// Ensures that the default allocation strategy is first fit to match DOS behavior.
    /// </summary>
    [Fact]
    public void DefaultAllocationStrategyIsFirstFit() {
        // Assert
        _memoryManager.AllocationStrategy.Should().Be(DosMemoryAllocationStrategy.FirstFit);
    }

    /// <summary>
    /// Ensures that the MCB chain check returns true for a valid chain.
    /// </summary>
    [Fact]
    public void CheckMcbChainValidChain() {
        // Arrange - create some allocations
        _memoryManager.AllocateMemoryBlock(1000);
        _memoryManager.AllocateMemoryBlock(2000);
        
        // Act
        bool isValid = _memoryManager.CheckMcbChain();
        
        // Assert
        isValid.Should().BeTrue();
    }

    /// <summary>
    /// Ensures that the MCB chain check returns false for a corrupted chain.
    /// </summary>
    [Fact]
    public void CheckMcbChainCorruptedChain() {
        // Arrange - create some allocations and then corrupt one
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(1000);
        block1.Should().NotBeNull();
        
        // Corrupt the MCB by setting an invalid TypeField (neither 'M' nor 'Z')
        block1!.TypeField = 0x00; // Invalid value
        
        // Act
        bool isValid = _memoryManager.CheckMcbChain();
        
        // Assert
        isValid.Should().BeFalse();
    }

    /// <summary>
    /// Ensures that FreeProcessMemory frees all blocks owned by a specific PSP.
    /// </summary>
    [Fact]
    public void FreeProcessMemoryFreesAllBlocks() {
        // Arrange
        DosMemoryControlBlock? block1 = _memoryManager.AllocateMemoryBlock(1000);
        ushort pspSegment = block1!.PspSegment;
        DosMemoryControlBlock? block2 = _memoryManager.AllocateMemoryBlock(2000);
        
        // Act
        bool result = _memoryManager.FreeProcessMemory(pspSegment);
        
        // Assert
        result.Should().BeTrue();
        block1.IsFree.Should().BeTrue();
        block2!.IsFree.Should().BeTrue();
    }

    /// <summary>
    /// Ensures that setting an invalid allocation strategy (fit type > 2) is ignored.
    /// </summary>
    [Fact]
    public void InvalidAllocationStrategyFitTypeIsIgnored() {
        // Arrange
        DosMemoryAllocationStrategy originalStrategy = _memoryManager.AllocationStrategy;
        
        // Act - try to set invalid fit type (0x03)
        _memoryManager.AllocationStrategy = (DosMemoryAllocationStrategy)0x03;
        
        // Assert - should remain unchanged
        _memoryManager.AllocationStrategy.Should().Be(originalStrategy);
    }

    /// <summary>
    /// Ensures that setting an invalid allocation strategy (bits 2-5 set) is ignored.
    /// </summary>
    [Fact]
    public void InvalidAllocationStrategyBits2To5SetIsIgnored() {
        // Arrange
        DosMemoryAllocationStrategy originalStrategy = _memoryManager.AllocationStrategy;
        
        // Act - try to set strategy with bit 2 set (0x04)
        _memoryManager.AllocationStrategy = (DosMemoryAllocationStrategy)0x04;
        
        // Assert - should remain unchanged
        _memoryManager.AllocationStrategy.Should().Be(originalStrategy);
    }

    /// <summary>
    /// Ensures that setting an invalid allocation strategy (invalid high memory bits) is ignored.
    /// </summary>
    [Fact]
    public void InvalidAllocationStrategyHighMemBitsIsIgnored() {
        // Arrange
        DosMemoryAllocationStrategy originalStrategy = _memoryManager.AllocationStrategy;
        
        // Act - try to set invalid high memory bits (0xC0 - both bits 6 and 7 set)
        _memoryManager.AllocationStrategy = (DosMemoryAllocationStrategy)0xC0;
        
        // Assert - should remain unchanged
        _memoryManager.AllocationStrategy.Should().Be(originalStrategy);
    }
}