namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Linq;

/// <summary>
/// Implements DOS memory operations, such as allocating and releasing MCBs.
/// </summary>
public class DosMemoryManager {
    internal const ushort LastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;
    private const ushort FakeMcbSize = 0xFFFF;
    private const byte FitTypeMask = 0x03;
    private const byte MaxValidFitType = 0x02;
    private const byte ReservedBitsMask = 0x3C;
    private const byte HighMemMask = 0xC0;
    private const byte HighMemFirstThenLow = 0x40;
    private const byte HighMemOnlyNoFallback = 0x80;
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly DosMemoryControlBlock _start;

    private readonly DosSwappableDataArea _sda;

    /// <summary>
    /// The current memory allocation strategy used for INT 21h/48h (allocate memory).
    /// </summary>
    /// <remarks>
    /// The default strategy is <see cref="DosMemoryAllocationStrategy.FirstFit"/> to match MS-DOS behavior.
    /// This can be changed via INT 21h/58h (Get/Set Memory Allocation Strategy).
    /// </remarks>
    private DosMemoryAllocationStrategy _allocationStrategy = DosMemoryAllocationStrategy.FirstFit;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="initialPspSegment">The initial PSP segment for MCB chain setup.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosMemoryManager(IMemory memory, ushort initialPspSegment, ILoggerService loggerService) {
        _loggerService = loggerService;
        _memory = memory;
        _sda = new(_memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));

        ushort pspSegment = initialPspSegment;
        // The MCB starts 1 paragraph (16 bytes) before the 16 paragraph (256 bytes) PSP. Since
        // we're the memory manager, we're the one who needs to read the MCB, so we need to start
        // with its address by subtracting 1 paragraph from the PSP.
        ushort loadSegment = (ushort)(pspSegment - 1);
        _start = GetDosMemoryControlBlockFromSegment(loadSegment);
        // LastFreeSegment and loadSegment are both valid segments that may be allocated, so we
        // need to add 1 paragraph to the result to ensure that our calculated size doesn't exclude
        // LastFreeSegment from being allocated. Some games do their own math to calculate the
        // maximum free conventional memory from the last block that was allocated rather than
        // asking the memory manager, and if we were off by one, allocation would fail.
        ushort size = (ushort)((LastFreeSegment - loadSegment) + 1);
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
    /// Gets or sets the current memory allocation strategy (INT 21h/58h).
    /// </summary>
    public DosMemoryAllocationStrategy AllocationStrategy {
        get => _allocationStrategy;
        set {
            // Validate the strategy - only allow valid combinations
            byte fitType = (byte)((byte)value & FitTypeMask);
            if (fitType > MaxValidFitType) {
                // Invalid fit type, ignore
                return;
            }
            // Validate bits 2-5 must be zero per DOS specification
            if (((byte)value & ReservedBitsMask) != 0) {
                return;
            }
            byte highMemBits = (byte)((byte)value & HighMemMask);
            if (highMemBits != 0x00 && highMemBits != HighMemFirstThenLow && highMemBits != HighMemOnlyNoFallback) {
                // Invalid high memory bits, ignore
                return;
            }
            _allocationStrategy = value;
        }
    }

    /// <summary>
    /// Allocates a memory block of the specified size. Returns <c>null</c> if no memory block could be found to fit the requested size.
    /// </summary>
    /// <param name="requestedSizeInParagraphs">The requested size in paragraphs of the memory block.</param>
    /// <returns>The allocated <see cref="DosMemoryControlBlock"/> or <c>null</c> if no memory block could be found.</returns>
    public DosMemoryControlBlock? AllocateMemoryBlock(ushort requestedSizeInParagraphs) {
        IEnumerable<DosMemoryControlBlock> candidates = FindCandidatesForAllocation(requestedSizeInParagraphs);

        // Select block based on allocation strategy
        DosMemoryControlBlock? blockOptional = SelectBlockByStrategy(candidates);
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
                _loggerService.Error("Could not split block {Block}", block);
            }
            return null;
        }

        block.PspSegment = _sda.CurrentProgramSegmentPrefix;
        return block;
    }

    /// <summary>
    /// Finds the largest free <see cref="DosMemoryControlBlock"/>.
    /// </summary>
    /// <returns>The largest free <see cref="DosMemoryControlBlock"/></returns>
    public DosMemoryControlBlock FindLargestFree() {
        return EnumerateBlocks()
            .Where(block => block.IsFree)
            .MaxBy(block => block.Size) ?? _start;
    }

    private IEnumerable<DosMemoryControlBlock> EnumerateBlocks() {
        DosMemoryControlBlock? current = _start;
        while (current != null) {
            yield return current;
            if (current.IsLast) {
                break;
            }
            current = current.GetNextOrDefault();
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
        return true;
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

        // Since the first thing we do is enlarge the block, we need to know the original size so
        // that we can restore it if we encounter an error later. We need to make sure that the
        // block doesn't grow to the maximum supported size on error.
        ushort initialBlockSizeInParagraphs = block.Size;

        // Make the block the biggest it can get
        if (!JoinBlocks(block, false)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Could not join MCB {Block}", block);
            }
            block = this.FindLargestFree();
            return DosErrorCode.InsufficientMemory;
        }

        if (block.Size < requestedSizeInParagraphs) {
            // Restore the original size of the block.
            if (block.Size != initialBlockSizeInParagraphs) {
                SplitBlock(block, initialBlockSizeInParagraphs);
            }

            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is too small for requested size {RequestedSize}",
                    block, requestedSizeInParagraphs);

                if (_loggerService.IsEnabled(LogEventLevel.Verbose) && !block.IsLast) {
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
        block.PspSegment = _sda.CurrentProgramSegmentPrefix;
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
        AllocRange size = CalculateSizeForExe(exeFile, pspSegment);

        // Since segment zero is well within the reserved space for interrupt vectors and BIOS data,
        // we use it as a sentinel to indicate that no specific segment address was requested, and
        // that we should just allocate the next available block where the program will fit.
        // Otherwise we try to allocate memory starting at the requested segment.
        DosMemoryControlBlock? block = (pspSegment == 0)
            // This is the normal, expected case during DOS LOAD/EXEC. We just ask the allocator to
            // find a block that will fit the maximum size requested by the EXE, and if it can't
            // find that, we ask it to find a block that fits the minimum required size. It doesn't
            // matter where that block is as long as it is in conventional memory.
            ? AllocateMemoryRange(size)
            // This is intended to be used when loading the initial program specified on the Spice86
            // command line. It always loads the program at the given address. It may fail if the
            // block isn't large enough even if there is a larger block available elsewhere that
            // would work. Therefore it is only recommended that you use this method when you load
            // the first program and the whole conventional memory space is available. You may use
            // it after that, but you should be well aware of the higher risk of allocation failing
            // than if you just allow the allocator to find the largest suitable block if you do so.
            : AllocateMemoryRange(pspSegment, size);

        if (block is not null) {
            // Since we know that we're allocating a memory block for a new program, and the PSP
            // always precedes the program image, set the PSP segment to the beginning of the block.
            // The current PSP segment in the PSP tracker that we normally use may be for the
            // program loading this one.
            block.PspSegment = block.DataBlockSegment;

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "Allocated {AllocationType} {SizeInParagraphs} paragraphs ({SizeInBytes} bytes) at {PspSegment} to load program",
                    block.Size == size.MinSizeInParagraphs ? "required" : "requested",
                    block.Size,
                    block.AllocationSizeInBytes,
                    ConvertUtils.ToHex16(block.DataBlockSegment));
            }
        } else if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error(
                "{SizeInParagraphs} paragraphs ({SizeInBytes} bytes) are not available at {PspSegment} to load program",
                size.MinSizeInParagraphs,
                size.MinSizeInParagraphs * 16,
                ConvertUtils.ToHex16(pspSegment));
        }

        return block;
    }

    /// <summary>
    /// Range the specifies a minimum and maximum size to allocate for a block of memory.
    /// </summary>
    /// <remarks>
    /// This is a helper type for the memory manager that is designed to support allocating a block
    /// at is at most the maximum size specified in this struct and at least the minimum size
    /// specified in this struct. It is primarily intended to support the min/max alloc concept in
    /// EXE files to allow the calculated values to be easily passed around together and to make it
    /// easier to identify where min/max allocation is requested inernally in the code.
    /// </remarks>
    private readonly record struct AllocRange {
        public AllocRange(ushort minSize, ushort maxSize) {
            MinSizeInParagraphs = minSize;
            MaxSizeInParagraphs = minSize > maxSize ? minSize : maxSize;
        }

        /// <summary>
        /// Minimum number of paragraphs that <em>must</em> be allocated.
        /// </summary>
        public ushort MinSizeInParagraphs { get; }

        /// <summary>
        /// Maximum number of paragraphs that <em>may</em> be allocated if they are available.
        /// </summary>
        public ushort MaxSizeInParagraphs { get; }
    }

    /// <summary>
    /// Calculates the minimum size required to load an EXE and the maximum requested size that it
    /// would like if it's available.
    /// </summary>
    /// <remarks>
    /// This function <em>does not</em> allocate any memory. It just calculate the required sizes
    /// that will need to be allocated. It's a member of this class rather than being calculated in
    /// the <see cref="DosExeFile"/> class because it requires insight into the current free memory
    /// blocks in some cases. It's more complicated that it may seem on the surface.
    /// </remarks>
    /// <param name="exeFile">EXE file header that defines the amount of space we need.</param>
    /// <param name="pspSegment">Segment address where the PSP before the EXE will be loaded.</param>
    /// <returns>The calculated minimum required and maximum requested allocation sizes.</returns>
    private AllocRange CalculateSizeForExe(DosExeFile exeFile, ushort pspSegment) {
        // Every program requires at least enough space for itself and the 16 paragraph (256 byte)
        // PSP that precedes it.
        ushort baseSizeInParagraphs = (ushort)(exeFile.ProgramSizeInParagraphsPerHeader + 0x10);

        ushort minSizeInParagraphs = (ushort)(baseSizeInParagraphs + exeFile.MinAlloc);
        ushort maxSizeInParagraphs = (ushort)(baseSizeInParagraphs + exeFile.MaxAlloc);
        // Also calculate maxSizeInParagraphs with overflow detection like FreeDOS
        uint maxSizeWithOverflow = (uint)baseSizeInParagraphs + exeFile.MaxAlloc;

        // If both the minimum and maximum allocation fields in the EXE header are cleared, DOS will
        // allocate the largest available block for it, and it will load the program image as high
        // as possible in memory. We don't need to worry about loading it. That's
        // DosProcessManager's job. We just need to make sure that we allocate the largest available
        // block correct in this case, and that it still meets the minimum required size for the PSP
        // and program image (our baseSizeInParagraphs). See the osdev wiki entry on the DOS EXE
        // format (wiki.osdev.org/MZ) for more information.
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0 || maxSizeWithOverflow > maxSizeInParagraphs) {
            ushort freeSizeInParagraphs = 0;
            if (pspSegment == 0) {
                // This is what real DOS does. It always finds the largest free block that it can
                // allocate. It's simple and relatively easy.
                DosMemoryControlBlock largestFreeBlock = FindLargestFree();
                if (largestFreeBlock.IsValid) {
                    freeSizeInParagraphs = largestFreeBlock.Size;
                }
            } else {
                // This behavior is unique to Spice86. Real DOS doesn't support loading a program at
                // a specific address. It always allocates a new block for it. However to support
                // loading a program at the location specified in the configuration at
                // initialization time, we support it. It's handy for reverse engineering to ensure
                // that the load address is always the same. Rather than finding the largest free
                // block, we just allocate the size of the block at the given address as long as it
                // is free.
                DosMemoryControlBlock requestedBlock = GetDosMemoryControlBlockFromSegment(
                    (ushort)(pspSegment - 1));
                if (requestedBlock.IsValid && requestedBlock.IsFree) {
                    freeSizeInParagraphs = requestedBlock.Size;
                }
            }

            // It's possible that we didn't find a suitable free block, or that the block that we
            // found isn't large enough to hold the PSP and the program image. Since the default
            // maxSizeInParagraphs already accounts for the PSP and program image, we'll just leave
            // it at that. Allocation will fail later on when it can't find enough space. That's
            // okay. We just need to ensure that the size we calculate isn't too small.
            if (freeSizeInParagraphs >= baseSizeInParagraphs) {
                maxSizeInParagraphs = freeSizeInParagraphs;
            }
        }

        return new AllocRange(minSizeInParagraphs, maxSizeInParagraphs);
    }

    /// <summary>
    /// Allocates a memory block that's at least as large as the minimum size but may be as large as
    /// the maximum size if there is a large enough free block available.
    /// </summary>
    /// <param name="size">The minimum/maximum size of the block to allocate.</param>
    /// <returns>
    /// The allocated <see cref="DosMemoryControlBlock"/>,
    /// or <c>null</c> if no memory block could be found.
    /// </returns>
    private DosMemoryControlBlock? AllocateMemoryRange(AllocRange size) {
        DosMemoryControlBlock? block = AllocateMemoryBlock(size.MaxSizeInParagraphs);
        if (block is not null) {
            return block;
        }

        block = FindLargestFree();
        if (!block.IsValid || block.Size < size.MinSizeInParagraphs) {
            return null;
        }

        block.PspSegment = _sda.CurrentProgramSegmentPrefix; // Marks the block as allocated.
        return block;
    }

    /// <summary>
    /// Allocates the memory block at the given segment and resizes it to be at least as large as
    /// the minimum required size but it may be up to the maximum requested size if there is enough
    /// free space available in the block.
    /// </summary>
    /// <remarks>
    /// The requested MCB <em>must</em> be free! If it is already allocated, it will not be resized
    /// and merged with the free space following it like <see cref="TryModifyBlock"/> would do. The
    /// allocation request will be rejected, and this function will return <c>null</c>.
    /// </remarks>
    /// <param name="blockSegment">The segment number of the MCB to allocate.</param>
    /// <param name="size">The minimum/maximum size of the block to allocate.</param>
    /// <returns>
    /// The allocated <see cref="DosMemoryControlBlock"/>,
    /// or <c>null</c> if the block was not valid, free, or large enough.
    /// </returns>
    private DosMemoryControlBlock? AllocateMemoryRange(ushort blockSegment, AllocRange size) {
        DosMemoryControlBlock block = GetDosMemoryControlBlockFromSegment((ushort)(blockSegment - 1));
        if (!CheckValidOrLogError(block)) {
            return null;
        } else if (!block.IsFree) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} cannot be allocated because it is not free", block);
            }
            return null;
        } else if (block.Size < size.MinSizeInParagraphs) {
            return null;
        }

        if (block.Size > size.MaxSizeInParagraphs) {
            SplitBlock(block, size.MaxSizeInParagraphs);
        }
        block.PspSegment = _sda.CurrentProgramSegmentPrefix; // Marks the block as allocated.
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

        // Mark the now unlinked MCB as "fake"
        next.Size = FakeMcbSize;
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

    /// <summary>
    /// Selects a memory block based on the current allocation strategy.
    /// </summary>
    /// <param name="candidates">List of candidate blocks that fit the requested size.</param>
    /// <returns>The selected block or null if none found.</returns>
    /// <remarks>
    /// Note: High memory bits (bits 6-7) of the allocation strategy are currently not handled.
    /// This method only implements low memory allocation strategies. UMB (Upper Memory Block)
    /// support would need to be added to handle strategies like FirstFitHighThenLow (0x40) or
    /// FirstFitHighOnlyNoFallback (0x80).
    /// </remarks>
    private DosMemoryControlBlock? SelectBlockByStrategy(IEnumerable<DosMemoryControlBlock> candidates) {
        // Get the fit type from the lower 2 bits of the strategy
        byte fitType = (byte)((byte)_allocationStrategy & FitTypeMask);

        DosMemoryControlBlock? selectedBlock = null;

        foreach (DosMemoryControlBlock current in candidates) {
            if (selectedBlock is null) {
                selectedBlock = current;
                // For first fit, we can return immediately
                if (fitType == (byte)DosMemoryAllocationStrategy.FirstFit) {
                    return selectedBlock;
                }
                continue;
            }

            switch (fitType) {
                case (byte)DosMemoryAllocationStrategy.FirstFit: // First fit - already returned above
                    break;

                case (byte)DosMemoryAllocationStrategy.BestFit: // Best fit - take the smallest
                    if (current.Size < selectedBlock.Size) {
                        selectedBlock = current;
                    }
                    break;

                case (byte)DosMemoryAllocationStrategy.LastFit: // Last fit - take the last one (highest address)
                    // Since we iterate from low to high addresses, always update to the current
                    selectedBlock = current;
                    break;
            }
        }

        return selectedBlock;
    }

    /// <summary>
    /// Checks the integrity of the MCB chain.
    /// </summary>
    /// <returns><c>true</c> if the MCB chain is valid, <c>false</c> if corruption is detected.</returns>
    public bool CheckMcbChain() {
        DosMemoryControlBlock? current = _start;

        while (current is not null) {
            if (!current.IsValid) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("MCB chain corrupted at segment {Segment}",
                        ConvertUtils.ToHex16(MemoryUtils.ToSegment(current.BaseAddress)));
                }
                return false;
            }

            if (current.IsLast) {
                return true;
            }

            current = current.GetNextOrDefault();
        }

        // If we get here, we reached the end of memory without finding MCB_LAST
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("MCB chain ended unexpectedly without MCB_LAST marker");
        }
        return false;
    }

    /// <summary>
    /// Frees all memory blocks owned by a specific PSP segment.
    /// </summary>
    /// <param name="pspSegment">The PSP segment whose memory should be freed.</param>
    /// <returns><c>true</c> if all blocks were freed successfully, <c>false</c> if an error occurred.</returns>
    public bool FreeProcessMemory(ushort pspSegment) {
        DosMemoryControlBlock? current = _start;

        while (current is not null) {
            if (!current.IsValid) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("MCB chain corrupted while freeing process memory");
                }
                return false;
            }

            // Free blocks owned by this PSP
            if (current.PspSegment == pspSegment) {
                current.SetFree();
                // Coalesce adjacent free blocks
                JoinBlocks(current, true);
            }

            if (current.IsLast) {
                break;
            }

            current = current.GetNextOrDefault();
        }

        return true;
    }

    /// <summary>
    /// Frees an environment block when the owning PSP terminates but stays resident.
    /// </summary>
    /// <param name="environmentSegment">Segment of the environment data block (PSP field).</param>
    /// <param name="ownerPspSegment">Segment of the PSP that owns the environment.</param>
    /// <returns><c>true</c> if the block was freed or no action was required.</returns>
    public bool FreeEnvironmentBlock(ushort environmentSegment, ushort ownerPspSegment) {
        if (environmentSegment == 0) {
            return true;
        }

        ushort mcbSegment = (ushort)(environmentSegment - 1);
        DosMemoryControlBlock block = GetDosMemoryControlBlockFromSegment(mcbSegment);
        if (!CheckValidOrLogError(block)) {
            return false;
        }

        if (block.PspSegment != ownerPspSegment) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "Environment block at {EnvSegment:X4} not owned by PSP {Owner:X4}, skipping free",
                    environmentSegment, ownerPspSegment);
            }
            return true;
        }

        block.SetFree();
        JoinBlocks(_start, true);
        return true;
    }
}
