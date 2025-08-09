namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Interfaces;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Implements DOS memory operations, such as allocating and releasing MCBs.
/// </summary>
public class DosMemoryManager {
    internal const ushort LastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly IDosPspManager _pspManager;
    private readonly DosMemoryControlBlock _start;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="pspManager">The class responsible to launch DOS programs and take care of the DOS PSP chain.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosMemoryManager(IMemory memory,
        IDosPspManager pspManager, ILoggerService loggerService) {
        _loggerService = loggerService;
        _pspManager = pspManager;
        _memory = memory;

        ushort pspSegment = _pspManager.GetCurrentPspSegment();
        // The MCB starts 1 paragraph (16 bytes) before the 16 paragraph (256 bytes) PSP. Since
        // we're the memory manager, we're the one who needs to read the MCB, so we need to start
        // with its address by subtracting 1 paragraph from the PSP.
        ushort startSegment = (ushort)(pspSegment - 1);
        _start = GetDosMemoryControlBlockFromSegment(startSegment);
        ushort size = (ushort)(LastFreeSegment - startSegment);
        // We adjusted the start address above so that it starts with the MCB, but the MCB itself
        // isn't actually useable space. We need it here in the DOS memory manager for accounting.
        // Therefore subtract the size of the MCB (1 paragraph, which is 16 bytes) from the total
        // size to get the useable space that we can allocate.
        _start.Size = (ushort)(size - 1);
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "DOS available memory: {ConventionalFree} - in paragraphs: {DosFreeParagraphs}",
                _start.AllocationSizeInBytes, _start.Size);
        }
        _start.SetFree();
        _start.SetLast();
    }

    /// <summary>
    /// Allocates a memory block of the specified size. Returns <c>null</c> if no memory block could be found to fit the requested size.
    /// </summary>
    /// <param name="requestedSizeInParagraphs">The requested size in paragraphs of the memory block.</param>
    /// <returns>The allocated <see cref="DosMemoryControlBlock"/> or <c>null</c> if no memory block could be found.</returns>
    public DosMemoryControlBlock? AllocateMemoryBlock(ushort requestedSizeInParagraphs) {
        IEnumerable<DosMemoryControlBlock> candidates = FindCandidatesForAllocation(requestedSizeInParagraphs);

        // take the smallest
        DosMemoryControlBlock? blockOptional = null;
        foreach (DosMemoryControlBlock currentElement in candidates) {
            if (blockOptional is null || currentElement.Size < blockOptional.Size) {
                blockOptional = currentElement;
            }
        }
        if (blockOptional is null) {
            // Nothing found
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Could not find any MCB to fit {RequestedSize}", requestedSizeInParagraphs);
            }
            return null;
        }

        DosMemoryControlBlock block = blockOptional;
        if (!SplitBlock(block, requestedSizeInParagraphs)) {
            // An issue occurred while splitting the block
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Could not spit block {Block}", block);
            }
            return null;
        }

        block.PspSegment = _pspManager.GetCurrentPspSegment();
        return block;
    }

    /// <summary>
    /// Finds the largest free <see cref="DosMemoryControlBlock"/>.
    /// </summary>
    /// <returns>The largest free <see cref="DosMemoryControlBlock"/></returns>
    public DosMemoryControlBlock FindLargestFree() {
        DosMemoryControlBlock? current = _start;
        DosMemoryControlBlock? largest = null;
        while (true) {
            if (current != null && current.IsFree && (largest == null || current.Size > largest.Size)) {
                largest = current;
            }

            if (current != null && current.IsLast && largest != null) {
                return largest;
            }

            if (current == null) {
                continue;
            }

            DosMemoryControlBlock? next = current.GetNextOrDefault();

            if (next is null) {
                return current;
            }

            current = next;
        }
    }

    /// <summary>
    /// Releases an MCB.
    /// </summary>
    /// <param name="blockSegment">The segment number of the MCB.</param>
    /// <returns>Whether the operation was successful.</returns>
    public bool FreeMemoryBlock(ushort blockSegment) {
        DosMemoryControlBlock block = GetDosMemoryControlBlockFromSegment(blockSegment);
        if (!CheckValidOrLogError(block)) {
            return false;
        }

        block.SetFree();
        return JoinBlocks(block, true);
    }

    /// <summary>
    /// Extends or reduces a MCB.
    /// </summary>
    /// <param name="blockSegment">The segment number of the MCB.</param>
    /// <param name="requestedSizeInParagraphs">The new size for the MCB, in paragraphs.</param>
    /// <param name="block">The mcb from the blockSegment, or the largest mcb found.</param>
    /// <returns>Whether the operation was successful.</returns>
    public DosErrorCode TryModifyBlock(in ushort blockSegment, in ushort requestedSizeInParagraphs,
        out DosMemoryControlBlock block) {
        block = GetDosMemoryControlBlockFromSegment((ushort)(blockSegment - 1));
        if (!CheckValidOrLogError(block)) {
            block = this.FindLargestFree();
            return DosErrorCode.MemoryControlBlockDestroyed;
        }

        // Make the block the biggest it can get
        if (!JoinBlocks(block, false)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Could not join MCB {Block}", block);
            }
            block = this.FindLargestFree();
            return DosErrorCode.InsufficientMemory;
        }

        if (block.Size < requestedSizeInParagraphs) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is too small for requested size {RequestedSize}",
                    block, requestedSizeInParagraphs);

                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    DosMemoryControlBlock? nextBlock = block.GetNextOrDefault();
                    _loggerService.Verbose("Next MCB is {Block}", nextBlock);
                }
            }
            block = this.FindLargestFree();
            return DosErrorCode.InsufficientMemory;
        }

        if (block.Size > requestedSizeInParagraphs) {
            SplitBlock(block, requestedSizeInParagraphs);
        }
        block.PspSegment = _pspManager.GetCurrentPspSegment();
        return DosErrorCode.NoError;
    }

    /// <summary>
    /// Reserves a memory block for the program and its stack.
    /// </summary>
    /// <remarks>
    /// <see cref="DosProcessManager"/> loads an executable into memory, creates the stack
    /// immediately above it, and sets up the initial stack segment and stack pointer to point to
    /// the top of the stack. Call this function immediately after it has been loaded (and before
    /// any other instructions have been executed so that the CPU state still points to the right
    /// place) to reserve the memory block where the program was loaded and its stack was
    /// created.<br/><br/>
    /// If we don't do this after load a program, the next time that this class is asked to allocate
    /// a block of memory, it will allocate a block that will allow the program to overwrite and
    /// corrupt parts of itself or its stack.<br/><br/>
    /// </remarks>
    /// <param name="cpuState">The CPU state with the allocated stack segment.</param>
    /// <returns>
    /// The <see cref="DosMemoryControlBlock"/> allocated for the program and its stack,
    /// or <c>null</c> if no memory block was allocated.
    /// </returns>
    public DosMemoryControlBlock? ReserveSpaceForExeAndStack(State cpuState) {
        // We will always have a stack allocated after loading an EXE, but not necessarily after
        // loading a COM file. That's okay. If we don't have it, we'll skip this reservation for
        // now. We'll likely have to revisit this and handle memory reservations for COM files some
        // day.
        if (cpuState.SS == 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "SS:SP={stackAddress} - skipping program and stack memory reservation",
                    new SegmentedAddress(cpuState.SS, cpuState.SP));
            }
            return null;
        }

        // After a program has been loaded but before it has been executed, its data segment will
        // point to the beginning of the PSP, which is the first structure in memory immediately
        // before the MCB. That's the beginning the block that we need to reserve.
        ushort pspSegment = cpuState.DS;

        // After the program has been loaded but before it has been executed, the SS:SP will
        // temporarily be the top of the stack that is the last memory address that we need to
        // reserve (because the stack grows down). The PSP that we retrieved above precedes the
        // program and will be the first memory address that we need to reserve. Knowing both makes
        // calculating the size easy.
        uint topOfStack = cpuState.StackPhysicalAddress;
        uint pspAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);
        if (topOfStack <= pspAddress) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(
                    "SS={ss} DS={ds} does not appear to point to a valid program to reserve space for",
                    ConvertUtils.ToHex16(cpuState.SS),
                    ConvertUtils.ToHex16(cpuState.DS));
            }
            return null;
        }
        // Add 1 paragraph (16 bytes) to the reserved space so that we include the top of the stack
        // in the reserved space as well. If we didn't do that, the top of the stack would be
        // clobbered with the MCB of the next free block that's created after we reserve this one.
        uint reservedSizeInBytes = (topOfStack - pspAddress) + 16;
        ushort reservedSizeInParagraphs = (ushort)(reservedSizeInBytes / 16);
        // The program that was loaded could be any size. If it didn't end on a paragraph boundary,
        // round up to the nearest paragraph to ensure that we always allocate enough space.
        if ((reservedSizeInBytes % 16) != 0) {
            reservedSizeInParagraphs++;
        }

        DosMemoryControlBlock block;
        DosErrorCode errorCode = TryModifyBlock(pspSegment, reservedSizeInParagraphs, out block);

        if (errorCode != DosErrorCode.NoError) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(
                    "Failed to reserve {reservedSizeInParagraphs} at {pspSegment} for the loaded program and its stack",
                    reservedSizeInParagraphs,
                    ConvertUtils.ToHex16(pspSegment));
            }
            return null;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose(
                "Reserved {reservedSizeInBytes} bytes ({reservedSizeInParagraphs} paragraphs) at {pspSegment} for the program and its stack",
                reservedSizeInBytes,
                reservedSizeInParagraphs,
                ConvertUtils.ToHex16(pspSegment));
        }

        return block;
    }

    private bool CheckValidOrLogError(DosMemoryControlBlock? block) {
        if (block is null || !block.IsValid) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is invalid", block);
            }
            return false;
        }

        return true;
    }

    private List<DosMemoryControlBlock> FindCandidatesForAllocation(int requestedSize) {
        DosMemoryControlBlock? current = _start;
        List<DosMemoryControlBlock> candidates = new();
        while (true) {
            if (!CheckValidOrLogError(current)) {
                return new List<DosMemoryControlBlock>();
            }
            JoinBlocks(current, true);
            if (current?.IsFree == true && current.Size >= requestedSize) {
                candidates.Add(current);
            }
            if (current?.IsLast == true) {
                return candidates;
            }

            DosMemoryControlBlock? next = current?.GetNextOrDefault();

            if (next is not null) {
                current = next;
            }
        }
    }

    private DosMemoryControlBlock GetDosMemoryControlBlockFromSegment(ushort blockSegment) {
        return new DosMemoryControlBlock(_memory, MemoryUtils.ToPhysicalAddress(blockSegment, 0));
    }

    private bool JoinBlocks(DosMemoryControlBlock? block, bool onlyIfFree) {
        if (onlyIfFree && block?.IsFree == false) {
            // Do not touch blocks in use
            return true;
        }

        while (block?.IsNonLast == true) {
            DosMemoryControlBlock? next = block.GetNextOrDefault();
            if (next is null || !next.IsFree) {
                // end of the free blocks reached
                break;
            }

            if (!CheckValidOrLogError(next)) {
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error("MCB {NextBlock} is not valid", next);
                }
                return false;
            }

            JoinContiguousBlocks(block, next);
        }

        return true;
    }

    private static void JoinContiguousBlocks(DosMemoryControlBlock destination, DosMemoryControlBlock next) {
        destination.TypeField = next.TypeField;

        // +1 because next block metadata is going to free space
        destination.Size = (ushort)(destination.Size + next.Size + 1);
    }

    /// <summary>
    /// Split the block:
    /// <ul>
    /// <li>If size is more than the block size => error, returns false</li>
    /// <li>If size matches the block size => nothing to do</li>
    /// <li>If size is less the block size => splits the block by creating a new free mcb at the end of the block</li>
    /// </ul>
    /// </summary>
    /// <param name="block">The block to split up.</param>
    /// <param name="size">The new size for the block.</param>
    /// <returns>Whether the operation was successful.</returns>
    private bool SplitBlock(DosMemoryControlBlock block, ushort size) {
        ushort blockSize = block.Size;
        if (blockSize == size) {
            // nothing to do
            return true;
        }

        int nextBlockSize = blockSize - size - 1;
        if (nextBlockSize < 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Cannot split block {Block} with size {Size} because it is too small",
                    block, size);
            }
            return false;
        }

        block.Size = size;
        DosMemoryControlBlock? next = block.GetNextOrDefault();

        if (next is null) {
            return false;
        }

        // if it was last propagate it
        next.TypeField = block.TypeField;

        // we are non last now for sure
        block.SetNonLast();

        // next is free
        next.SetFree();
        next.Size = (ushort)nextBlockSize;
        return true;
    }
}