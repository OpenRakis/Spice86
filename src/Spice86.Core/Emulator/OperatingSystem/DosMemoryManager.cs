namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.LoadableFile.Dos;
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
        return FreeMemoryBlock(GetDosMemoryControlBlockFromSegment(blockSegment));
    }

    /// <summary>
    /// Releases an MCB.
    /// </summary>
    /// <param name="block">The MCB to free.</param>
    /// <returns>Whether the operation was successful.</returns>
    public bool FreeMemoryBlock(DosMemoryControlBlock block) {
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
        block.PspSegment = _pspTracker.GetCurrentPspSegment();
        return DosErrorCode.NoError;
    }

    /// <summary>
    /// Reserves a memory block for an executable.
    /// </summary>
    /// <remarks>
    /// <see cref="DosProcessManager"/> needs to allocate space for the programs that it loads into
    /// memory. For COM files that's fairly straight-forward since it's just the PSP, a fixed
    /// offset, and the size of the COM file itself. That makes it easy to just use the normal
    /// allocation functions in this class. However EXE files are more complex. They require space
    /// in memory for the PSP, the EXE itself (minus the header), its stack, and any extra memory
    /// that the EXE may optionally request in its header. To further complicate matters, the EXE
    /// may not request any extra memory, or it may request anywhere between a minimum and a maximum
    /// amount. DOS is supposed to allocate as much as it can from that requested extra memory
    /// allocation, but no less than the minimum amount. This function does that
    /// allocation.<br/><br/>
    /// Since determining how much memory is needed to load the EXE and what the largest block
    /// available that can fulfill it requires knowledge of both the EXE header and the available
    /// conventional memory space, this function is implemented in the memory manager so that it can
    /// more easily determine the appropriate allocation for the EXE and allocate a new block of
    /// memory for it. That also has the side-effect of making it easier to unit test the more
    /// complicated logic of allocating the correct amount of memory without involving the process
    /// manager or actually loading the executable into memory.
    /// </remarks>
    /// <param name="exeFile">EXE file header that defines the amount of space we need.</param>
    /// <param name="pspSegment">Segment address where the PSP before the EXE will be loaded.</param>
    /// <returns>
    /// The <see cref="DosMemoryControlBlock"/> allocated for the program and its stack,
    /// or <c>null</c> if no memory block was allocated.
    /// </returns>
    public DosMemoryControlBlock? ReserveSpaceForExe(DosExeFile exeFile, ushort pspSegment = 0) {
        // Every program requires at least enough space for itself and the 16 paragraph (256 byte)
        // PSP that precedes it.
        ushort baseSizeInParagraphs = (ushort)(exeFile.ProgramSizeInParagraphs + 0x10);

        ushort minSizeInParagraphs = (ushort)(baseSizeInParagraphs + exeFile.MinAlloc);
        ushort maxSizeInParagraphs = (ushort)(baseSizeInParagraphs + exeFile.MaxAlloc);

        // If both the minimum and maximum allocation fields in the EXE header are cleared, DOS will
        // allocate the largest available block for it, and it will load the program image as high
        // as possible in memory. We don't need to worry about loading it. That's
        // DosProcessManager's job. We just need to make sure that we allocate the largest available
        // block correct in this case, and that it still meets the minimum required size for the PSP
        // and program image (our baseSizeInParagraphs). See the osdev wiki entry on the DOS EXE
        // format (wiki.osdev.org/MZ) for more information.
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
            ushort freeSizeInParagraphs = 0;
            if (pspSegment == 0) {
                DosMemoryControlBlock largestFreeBlock = FindLargestFree();
                if (largestFreeBlock.IsValid) {
                    freeSizeInParagraphs = largestFreeBlock.Size;
                }
            } else {
                DosMemoryControlBlock requestedBlock = GetDosMemoryControlBlockFromSegment((ushort)(pspSegment - 1));
                if (requestedBlock.IsValid) {
                    freeSizeInParagraphs = requestedBlock.Size;
                    if (!requestedBlock.IsFree) {
                        DosMemoryControlBlock? nextBlock = requestedBlock.GetNextOrDefault();
                        if (nextBlock is not null && nextBlock.IsValid && nextBlock.IsFree) {
                            freeSizeInParagraphs += nextBlock.Size;
                        }
                    }
                }
            }
            if (freeSizeInParagraphs >= baseSizeInParagraphs) {
                maxSizeInParagraphs = freeSizeInParagraphs;
            }
        }

        DosMemoryControlBlock? block = null;

        // Since segment zero is well within the reserved space for interrupt vectors and BIOS data,
        // we use it as a sentinel to indicate that no specific segment address was requested, and
        // that we should just allocate the next available block where the program will fit.
        // Otherwise we try to allocate memory starting at the requested segment.
        if (pspSegment == 0) {
            block = AllocateMemoryBlock(maxSizeInParagraphs);
            if (block is null && minSizeInParagraphs < maxSizeInParagraphs) {
                block = AllocateMemoryBlock(minSizeInParagraphs);
            }
        } else {
            DosMemoryControlBlock tryBlock;
            DosErrorCode errorCode = TryModifyBlock(pspSegment, maxSizeInParagraphs, out tryBlock);
            if (errorCode == DosErrorCode.NoError) {
                block = tryBlock;
            } else if (minSizeInParagraphs < maxSizeInParagraphs) {
                errorCode = TryModifyBlock(pspSegment, minSizeInParagraphs, out tryBlock);
                if (errorCode == DosErrorCode.NoError) {
                    block = tryBlock;
                }
            }
        }

        if (block is not null) {
            // Since we know that we're allocating a memory block for a new program, and the PSP
            // always precedes the program image, set the PSP segment to the beginning of the block.
            // The current PSP segment in the PSP tracker that we normally use may be for the
            // program loading this one.
            block.PspSegment = block.DataBlockSegment;

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "Allocated {AllocationType} {SizeInParagraphs} paragraphs ({SizeInBytes} bytes) at {PspSegment} to load program",
                    block.Size == minSizeInParagraphs ? "required" : "requested",
                    block.Size,
                    block.AllocationSizeInBytes,
                    ConvertUtils.ToHex16(block.DataBlockSegment));
            }
        } else if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error(
                "{SizeInParagraphs} paragraphs ({SizeInBytes} bytes) are not available at {PspSegment} to load program",
                minSizeInParagraphs,
                minSizeInParagraphs * 16,
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