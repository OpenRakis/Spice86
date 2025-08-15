namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Implements DOS memory operations, such as allocating and releasing MCBs.
/// </summary>
public class DosMemoryManager {
    internal const ushort LastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryControlBlock _start;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="pspTracker">The class responsible for DOS program loader configuration.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosMemoryManager(IMemory memory,
        DosProgramSegmentPrefixTracker pspTracker, ILoggerService loggerService) {
        _loggerService = loggerService;
        _pspTracker = pspTracker;
        _memory = memory;

        ushort pspSegment = _pspTracker.InitialPspSegment;
        ushort startSegment = (ushort)(pspSegment - 1);
        _start = GetDosMemoryControlBlockFromSegment(startSegment);
        ushort size = (ushort)(LastFreeSegment - startSegment);
        // size -1 because the mcb itself takes 16 bytes which is 1 paragraph
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

        block.PspSegment = _pspTracker.GetCurrentPspSegment();
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

        if (block.Size < requestedSizeInParagraphs - 1) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is too small for requested size {RequestedSize}",
                    block.Size, requestedSizeInParagraphs);
            }
            block = this.FindLargestFree();
            return DosErrorCode.InsufficientMemory;
        }

        if (block.Size > requestedSizeInParagraphs) {
            SplitBlock(block, requestedSizeInParagraphs);
        }
        block.PspSegment = _pspTracker.GetCurrentPspSegment();
        return DosErrorCode.NoError;
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