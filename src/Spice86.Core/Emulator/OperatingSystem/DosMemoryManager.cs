using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.OperatingSystem;


using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Linq;
using System.Text;

/// <summary>
/// Implements DOS memory operations, such as allocating and releasing MCBs.
/// </summary>
public class DosMemoryManager {
    internal const ushort LastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;
    private const ushort UmbChainStartSegment = 0x9FFF;
    private const ushort FirstUmbMcbSegment = 0xD000;
    private const ushort UmbBridgeOwner = 0x0008;
    private const ushort UmbWithoutEmsParagraphs = 0x2000;
    private const ushort UmbWithEmsParagraphs = 0x1000;
    private const ushort FakeMcbSize = 0xFFFF;
    private const byte FitTypeMask = 0x03;
    private const byte MaxValidFitType = 0x02;
    private const byte HighMemMask = 0xC0;
    private const byte HighMemFirstThenLow = 0x80;
    private readonly ILogger _loggerService;
    private readonly IMemory _memory;
    private readonly DosMemoryControlBlock _start;
    private readonly DosMemoryControlBlock? _umbChainStart;
    private readonly DosSysVars? _dosSysVars;

    private readonly DosSwappableDataArea _sda;

    /// <summary>
    /// The current memory allocation strategy used for INT 21h/48h (allocate memory).
    /// </summary>
    /// <remarks>
    /// The default strategy is <see cref="DosMemoryAllocationStrategy.FirstFit"/> to match MS-DOS behavior.
    /// This can be changed via INT 21h/58h (Get/Set Memory Allocation Strategy).
    /// </remarks>
    private ushort _allocationStrategy = (ushort)DosMemoryAllocationStrategy.FirstFit;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="initialPspSegment">The initial PSP segment for MCB chain setup.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosMemoryManager(IMemory memory, ushort initialPspSegment, ILogger loggerService)
        : this(memory, initialPspSegment, loggerService, new UmbSetup(null, false, false)) {
    }

    /// <summary>
    /// Initializes DOS memory with an optional DOSBox-compatible UMB chain.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="initialPspSegment">The initial PSP segment for MCB chain setup.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dosSysVars">The DOS InfoBlock containing UMB chain state.</param>
    /// <param name="umbEnabled">Whether UMBs should be created.</param>
    /// <param name="emsActive">Whether EMS reserves the upper half of the UMB area.</param>
    public DosMemoryManager(IMemory memory, ushort initialPspSegment, ILogger loggerService,
        DosSysVars dosSysVars, bool umbEnabled, bool emsActive)
        : this(memory, initialPspSegment, loggerService, new UmbSetup(dosSysVars, umbEnabled, emsActive)) {
    }

    private DosMemoryManager(IMemory memory, ushort initialPspSegment, ILogger loggerService, UmbSetup umbSetup) {
        _loggerService = loggerService;
        _memory = memory;
        _sda = new(_memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        _dosSysVars = umbSetup.DosSysVars;

        ushort pspSegment = initialPspSegment;
        // The MCB starts 1 paragraph (16 bytes) before the 16 paragraph (256 bytes) PSP. Since
        // we're the memory manager, we're the one who needs to read the MCB, so we need to start
        // with its address by subtracting 1 paragraph from the PSP.
        ushort loadSegment = (ushort)(pspSegment - 1);
        _start = GetDosMemoryControlBlockFromSegment(loadSegment);
        ushort size;
        if (umbSetup.IsEnabled) {
            size = (ushort)(UmbChainStartSegment - loadSegment - 1);
        } else {
            // LastFreeSegment and loadSegment are both valid segments that may be allocated, so we
            // need to add 1 paragraph to the result to ensure that our calculated size doesn't exclude
            // LastFreeSegment from being allocated. Some games do their own math to calculate the
            // maximum free conventional memory from the last block that was allocated rather than
            // asking the memory manager, and if we were off by one, allocation would fail.
            size = (ushort)((LastFreeSegment - loadSegment) + 1);
            // The MCB itself isn't usable space, so subtract its paragraph from the total.
            size = (ushort)(size - 1);
        }
        _start.Size = size;
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation(
                "DOS available memory: {ConventionalFree} - in paragraphs: {DosFreeParagraphs}",
                _start.AllocationSizeInBytes, _start.Size);
        }
        _start.SetFree();
        _start.SetLast();

        if (umbSetup.IsEnabled) {
            _umbChainStart = GetDosMemoryControlBlockFromSegment(UmbChainStartSegment);
            _umbChainStart.PspSegment = UmbBridgeOwner;
            _umbChainStart.Size = (ushort)(FirstUmbMcbSegment - UmbChainStartSegment - 1);
            _umbChainStart.SetNonLast();
            _umbChainStart.SetOwnerName("SC");

            DosMemoryControlBlock firstUmb = GetDosMemoryControlBlockFromSegment(FirstUmbMcbSegment);
            firstUmb.SetFree();
            firstUmb.Size = (ushort)((umbSetup.EmsActive ? UmbWithEmsParagraphs : UmbWithoutEmsParagraphs) - 1);
            firstUmb.SetLast();
            if (_dosSysVars is not null) {
                _dosSysVars.StartOfUMBChain = UmbChainStartSegment;
                _dosSysVars.ChainingUMB = 0;
            }
        } else {
            _umbChainStart = null;
            if (_dosSysVars is not null) {
                _dosSysVars.StartOfUMBChain = 0xFFFF;
                _dosSysVars.ChainingUMB = 0;
            }
        }

        // Initial detailed memory map for diagnostics
        LogMemoryGraphic("startup");
    }

    private readonly record struct UmbSetup(DosSysVars? DosSysVars, bool IsEnabled, bool EmsActive);

    /// <summary>
    /// Gets whether an upper-memory chain was created.
    /// </summary>
    public bool HasUpperMemoryBlocks => _umbChainStart is not null;

    /// <summary>
    /// Gets the low bit of the DOS UMB chain link state.
    /// </summary>
    public byte UmbChainState => (byte)((_dosSysVars?.ChainingUMB ?? 0) & 1);

    /// <summary>
    /// Links or unlinks the UMB chain from the conventional MCB chain.
    /// </summary>
    /// <param name="linkState">Zero to unlink, one to link.</param>
    /// <returns>Whether the requested link state was applied.</returns>
    public bool SetUmbChainLinkState(ushort linkState) {
        if (!HasUpperMemoryBlocks || _dosSysVars is null || linkState > 1) {
            return false;
        }
        if (UmbChainState == linkState) {
            return true;
        }

        DosMemoryControlBlock current = _start;
        while (true) {
            ushort currentSegment = MemoryUtils.ToSegment(current.BaseAddress);
            ushort nextSegment = (ushort)(currentSegment + current.Size + 1);
            if (nextSegment == UmbChainStartSegment) {
                if (linkState == 1) {
                    current.SetNonLast();
                } else {
                    current.SetLast();
                }
                _dosSysVars.ChainingUMB = (byte)linkState;
                return true;
            }

            if (current.IsLast) {
                return false;
            }

            DosMemoryControlBlock? next = current.GetNextOrDefault();
            if (next is null || !next.IsValid) {
                return false;
            }
            current = next;
        }
    }

    /// <summary>
    /// Gets or sets the current memory allocation strategy (INT 21h/58h).
    /// </summary>
    public DosMemoryAllocationStrategy AllocationStrategy {
        get => (DosMemoryAllocationStrategy)_allocationStrategy;
        set {
            TrySetAllocationStrategy((ushort)value);
        }
    }

    /// <summary>
    /// Gets the full allocation strategy value returned by INT 21h/58h.
    /// </summary>
    public ushort AllocationStrategyValue => _allocationStrategy;

    /// <summary>
    /// Validates and updates the DOS memory allocation strategy.
    /// </summary>
    /// <param name="strategy">The strategy value from BX.</param>
    /// <returns>Whether the strategy was accepted.</returns>
    public bool TrySetAllocationStrategy(ushort strategy) {
        if ((strategy & 0x3F) > MaxValidFitType) {
            return false;
        }
        _allocationStrategy = strategy;
        return true;
    }

    /// <summary>
    /// Allocates a memory block of the specified size. Returns <c>null</c> if no memory block could be found to fit the requested size.
    /// </summary>
    /// <param name="requestedSizeInParagraphs">The requested size in paragraphs of the memory block.</param>
    /// <returns>The allocated <see cref="DosMemoryControlBlock"/> or <c>null</c> if no memory block could be found.</returns>
    public DosMemoryControlBlock? AllocateMemoryBlock(ushort requestedSizeInParagraphs) {
        LogMemoryGraphic($"AllocateMemoryBlock - before requested:{requestedSizeInParagraphs}");
        IEnumerable<DosMemoryControlBlock> candidates = FindCandidatesForAllocation(requestedSizeInParagraphs);

        // Select block based on allocation strategy
        DosMemoryControlBlock? blockOptional = SelectBlockByStrategy(candidates);
        if (blockOptional is null) {
            // Nothing found
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError("Could not find any MCB to fit {RequestedSize}", requestedSizeInParagraphs);
            }
            LogMemoryGraphic($"AllocateMemoryBlock - failed requested:{requestedSizeInParagraphs}");
            return null;
        }

        DosMemoryControlBlock block = blockOptional;
        byte fitType = (byte)(_allocationStrategy & FitTypeMask);
        if (fitType == (byte)DosMemoryAllocationStrategy.LastFit) {
            DosMemoryControlBlock? allocatedBlock = SplitBlockFromEnd(block, requestedSizeInParagraphs);
            if (allocatedBlock is null) {
                if (_loggerService.IsEnabled(LogLevel.Error)) {
                    _loggerService.LogError("Could not split block {Block}", block);
                }
                LogMemoryGraphic($"AllocateMemoryBlock - split_failed block:{ConvertUtils.ToHex16(block.DataBlockSegment)}");
                return null;
            }
            block = allocatedBlock;
        } else if (!SplitBlock(block, requestedSizeInParagraphs)) {
            // An issue occurred while splitting the block
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError("Could not split block {Block}", block);
            }
            LogMemoryGraphic($"AllocateMemoryBlock - split_failed block:{ConvertUtils.ToHex16(block.DataBlockSegment)}");
            return null;
        }

        block.PspSegment = _sda.CurrentProgramSegmentPrefix;
        LogMemoryGraphic($"AllocateMemoryBlock - allocated seg:{ConvertUtils.ToHex16(block.DataBlockSegment)} size:{block.Size} psp:{ConvertUtils.ToHex16(block.PspSegment)}");
        return block;
    }

    /// <summary>
    /// Finds the largest free <see cref="DosMemoryControlBlock"/>.
    /// </summary>
    /// <returns>The largest free <see cref="DosMemoryControlBlock"/></returns>
    public DosMemoryControlBlock FindLargestFree() {
        LogMemoryGraphic("FindLargestFree - before");
        DosMemoryControlBlock res = EnumerateAllocationBlocks()
            .Where(block => block.IsFree)
            .MaxBy(block => block.Size) ?? _start;
        LogMemoryGraphic("FindLargestFree - after");
        return res;
    }

    /// <summary>
    /// Finds the largest free block considered by the current allocation strategy.
    /// </summary>
    /// <returns>The largest free block size, or zero if the strategy's chains have no free blocks.</returns>
    public ushort FindLargestFreeSizeForAllocation() {
        return EnumerateAllocationBlocks()
            .Where(static block => block.IsFree)
            .Select(static block => block.Size)
            .DefaultIfEmpty((ushort)0)
            .Max();
    }

    /// <summary>
    /// Walks the MCB chain starting from the first block, yielding each MCB in order
    /// until the last block is reached. Intended for read-only inspection.
    /// </summary>
    public IEnumerable<DosMemoryControlBlock> EnumerateBlocks() {
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
        LogMemoryGraphic($"FreeMemoryBlock - before seg:{ConvertUtils.ToHex16(block.DataBlockSegment)}");
        if (!CheckValidOrLogError(block)) {
            LogMemoryGraphic($"FreeMemoryBlock - invalid seg:{ConvertUtils.ToHex16(block.DataBlockSegment)}");
            return false;
        }

        block.SetFree();
        LogMemoryGraphic($"FreeMemoryBlock - after freed seg:{ConvertUtils.ToHex16(block.DataBlockSegment)}");
        return true;
    }

    /// <summary>
    /// Releases an allocated block in the UMB chain.
    /// </summary>
    /// <param name="dataBlockSegment">The segment address of the UMB data block.</param>
    /// <returns><c>true</c> if an allocated UMB was released; otherwise, <c>false</c>.</returns>
    public bool FreeUpperMemoryBlock(ushort dataBlockSegment) {
        if (_umbChainStart is null || dataBlockSegment == 0) {
            return false;
        }

        DosMemoryControlBlock? current = _umbChainStart;
        bool isBridge = true;
        while (current is not null) {
            if (!CheckValidOrLogError(current)) {
                return false;
            }

            if (!isBridge && current.DataBlockSegment == dataBlockSegment) {
                return !current.IsFree && FreeMemoryBlock(current);
            }

            if (current.IsLast) {
                break;
            }
            current = current.GetNextOrDefault();
            isBridge = false;
        }
        return false;
    }

    /// <summary>
    /// Extends or reduces a MCB.
    /// </summary>
    /// <param name="blockSegment">The segment number of the MCB.</param>
    /// <param name="requestedSizeInParagraphs">The new size for the MCB, in paragraphs.</param>
    /// <param name="block">The mcb from the blockSegment, or the largest mcb found.</param>
    /// <returns>Whether the operation was successful.</returns>
    public DosErrorCode ModifyBlock(ushort blockSegment, ushort requestedSizeInParagraphs, out DosMemoryControlBlock block) {
        return TryModifyBlock(blockSegment, requestedSizeInParagraphs, out block);
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
        LogMemoryGraphic($"TryModifyBlock - start seg:{ConvertUtils.ToHex16(blockSegment)} req:{requestedSizeInParagraphs}");
        block = GetDosMemoryControlBlockFromSegment((ushort)(blockSegment - 1));
        ushort newSizeInParagraphs = requestedSizeInParagraphs;

        if (!CheckValidOrLogError(block)) {
            LogMemoryGraphic($"TryModifyBlock - invalid mcb seg:{ConvertUtils.ToHex16(blockSegment)}");
            return DosErrorCode.MemoryControlBlockDestroyed;
        }

        //AlphaWaves loader starts a TSR for adlib-sound that wrongly sets the block-size in DX leaving BX = 0
        if (newSizeInParagraphs == 0 && blockSegment == _sda.CurrentProgramSegmentPrefix) {
            newSizeInParagraphs = DosProgramSegmentPrefix.PspSizeInParagraphs;
        }

        // Make the block the biggest it can get
        if (!JoinBlocks(block, false)) {
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError("Could not join MCB {Block}", block);
            }
            LogMemoryGraphic($"TryModifyBlock - join_failed seg:{ConvertUtils.ToHex16(block.DataBlockSegment)}");
            return DosErrorCode.InsufficientMemory;
        }

        if (block.Size < newSizeInParagraphs) {
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError("MCB {Block} is too small for requested size {RequestedSize}",
                    block, newSizeInParagraphs);

                if (_loggerService.IsEnabled(LogLevel.Trace) && !block.IsLast) {
                    DosMemoryControlBlock? nextBlock = block.GetNextOrDefault();
                    _loggerService.LogTrace("Next MCB is {Block}", nextBlock);
                }
            }
            LogMemoryGraphic($"TryModifyBlock - too_small seg:{ConvertUtils.ToHex16(block.DataBlockSegment)} req:{newSizeInParagraphs} size:{block.Size}");
            return DosErrorCode.InsufficientMemory;
        }

        if (block.Size > newSizeInParagraphs) {
            SplitBlock(block, newSizeInParagraphs);
        }
        block.PspSegment = _sda.CurrentProgramSegmentPrefix;
        LogMemoryGraphic($"TryModifyBlock - success seg:{ConvertUtils.ToHex16(block.DataBlockSegment)} size:{block.Size}");
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

            if (_loggerService.IsEnabled(LogLevel.Trace)) {
                _loggerService.LogTrace(
                    "Allocated {AllocationType} {SizeInParagraphs} paragraphs ({SizeInBytes} bytes) at {PspSegment} to load program",
                    block.Size == size.MinSizeInParagraphs ? "required" : "requested",
                    block.Size,
                    block.AllocationSizeInBytes,
                    ConvertUtils.ToHex16(block.DataBlockSegment));
            }
        } else if (_loggerService.IsEnabled(LogLevel.Error)) {
            _loggerService.LogError(
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
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError("MCB {Block} cannot be allocated because it is not free", block);
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
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError("MCB {Block} is invalid", block);
            }
            return false;
        }

        return true;
    }

    private List<DosMemoryControlBlock> FindCandidatesForAllocation(int requestedSize) {
        List<DosMemoryControlBlock> candidates = new();
        byte highMemoryStrategy = (byte)(_allocationStrategy & HighMemMask);
        if (highMemoryStrategy != 0 && _umbChainStart is not null) {
            AddAllocationCandidates(_umbChainStart, requestedSize, UmbChainState != 0, candidates);
            if ((highMemoryStrategy & HighMemFirstThenLow) == 0) {
                return candidates;
            }
        }
        AddAllocationCandidates(_start, requestedSize, true, candidates);
        return candidates;
    }

    private void AddAllocationCandidates(DosMemoryControlBlock start, int requestedSize,
        bool compressChain, List<DosMemoryControlBlock> candidates) {
        DosMemoryControlBlock? current = start;
        while (current is not null) {
            if (!CheckValidOrLogError(current)) {
                candidates.Clear();
                return;
            }
            if (compressChain) {
                JoinBlocks(current, true);
            }
            if (current.IsFree && current.Size >= requestedSize) {
                candidates.Add(current);
            }
            if (current.IsLast) {
                return;
            }
            current = current.GetNextOrDefault();
        }
    }

    private IEnumerable<DosMemoryControlBlock> EnumerateAllocationBlocks() {
        byte highMemoryStrategy = (byte)(_allocationStrategy & HighMemMask);
        if (highMemoryStrategy != 0 && _umbChainStart is not null) {
            foreach (DosMemoryControlBlock block in EnumerateChain(_umbChainStart)) {
                yield return block;
            }
            if ((highMemoryStrategy & HighMemFirstThenLow) == 0) {
                yield break;
            }
        }
        foreach (DosMemoryControlBlock block in EnumerateChain(_start)) {
            yield return block;
        }
    }

    private static IEnumerable<DosMemoryControlBlock> EnumerateChain(DosMemoryControlBlock start) {
        DosMemoryControlBlock? current = start;
        while (current is not null) {
            yield return current;
            if (current.IsLast) {
                yield break;
            }
            current = current.GetNextOrDefault();
        }
    }

    private DosMemoryControlBlock GetDosMemoryControlBlockFromSegment(ushort blockSegment) {
        return new DosMemoryControlBlock(_memory, MemoryUtils.ToPhysicalAddress(blockSegment, 0));
    }

    /// <summary>
    /// Dumps an ASCII memory map at debug level for diagnostic comparison across calls.
    /// </summary>
    /// <param name="context">A short context string indicating the caller or event.</param>
    private void LogMemoryGraphic(string context) {
        if (!_loggerService.IsEnabled(LogLevel.Debug)) {
            return;
        }

        var blocks = EnumerateBlocks().ToList();
        if (blocks.Count == 0) {
            _loggerService.LogDebug("DOS Memory Map ({Context}): no blocks", context);
            return;
        }

        int totalParagraphs = blocks.Sum(b => (int)b.Size + 1);
        int width = 96; // fixed output width for comparability
        int paragraphsPerChar = Math.Max(1, (int)Math.Ceiling(totalParagraphs / (double)width));

        var sb = new StringBuilder();
        sb.AppendLine($"DOS Memory Map ({context}) total paragraphs: {totalParagraphs} (par/char~{paragraphsPerChar})");

        var bar = new StringBuilder();
        foreach (var b in blocks) {
            int blockParagraphs = (int)b.Size + 1;
            int chars = Math.Max(1, (int)Math.Round(blockParagraphs / (double)paragraphsPerChar));
            char ch;
            if (!b.IsValid) {
                ch = '!';
            } else if (b.Size == FakeMcbSize) {
                ch = 'x';
            } else if (b.IsFree) {
                ch = '.';
            } else {
                ch = '#';
            }

            for (int i = 0; i < chars; i++) {
                bar.Append(ch);
            }
        }

        sb.AppendLine(bar.ToString());

        // Detailed legend per block to match positions roughly. Include start segment and size
        foreach (var b in blocks) {
            string seg = ConvertUtils.ToHex16(b.DataBlockSegment);
            string psp = ConvertUtils.ToHex16(b.PspSegment);
            string owner = string.IsNullOrEmpty(b.Owner) ? "" : b.Owner.Trim();
            sb.AppendLine($"{seg} | {(b.IsFree ? "FREE " : "USED ")} Size:{b.Size,5} par Owner:{owner,-8} PSP:{psp} Last:{b.IsLast}");
        }

        _loggerService.LogDebug(sb.ToString());
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
                if (_loggerService.IsEnabled(LogLevel.Error)) {
                    _loggerService.LogError("MCB {NextBlock} is not valid", next);
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
            if (_loggerService.IsEnabled(LogLevel.Error)) {
                _loggerService.LogError("Cannot split block {Block} with size {Size} because it is too small",
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

    private DosMemoryControlBlock? SplitBlockFromEnd(DosMemoryControlBlock block, ushort size) {
        ushort blockSize = block.Size;
        if (blockSize < size) {
            return null;
        }
        if (blockSize == size) {
            return block;
        }

        ushort mcbSegment = MemoryUtils.ToSegment(block.BaseAddress);
        ushort allocationMcbSegment = (ushort)(mcbSegment + blockSize - size);
        byte originalType = block.TypeField;
        block.Size = (ushort)(blockSize - size - 1);
        block.SetNonLast();
        block.SetFree();

        DosMemoryControlBlock allocatedBlock = GetDosMemoryControlBlockFromSegment(allocationMcbSegment);
        allocatedBlock.TypeField = originalType;
        allocatedBlock.Size = size;
        return allocatedBlock;
    }

    /// <summary>
    /// Selects a memory block based on the current allocation strategy.
    /// </summary>
    /// <param name="candidates">List of candidate blocks that fit the requested size.</param>
    /// <returns>The selected block or null if none found.</returns>
    private DosMemoryControlBlock? SelectBlockByStrategy(IEnumerable<DosMemoryControlBlock> candidates) {
        // Get the fit type from the lower 2 bits of the strategy
        byte fitType = (byte)(_allocationStrategy & FitTypeMask);

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
                if (_loggerService.IsEnabled(LogLevel.Error)) {
                    _loggerService.LogError("MCB chain corrupted at segment {Segment}",
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
        if (_loggerService.IsEnabled(LogLevel.Error)) {
            _loggerService.LogError("MCB chain ended unexpectedly without MCB_LAST marker");
        }
        return false;
    }

    /// <summary>
    /// Frees all memory blocks owned by a specific PSP segment.
    /// </summary>
    /// <param name="pspSegment">The PSP segment whose memory should be freed.</param>
    /// <returns><c>true</c> if all blocks were freed successfully, <c>false</c> if an error occurred.</returns>
    public bool FreeProcessMemory(ushort pspSegment) {
        LogMemoryGraphic($"FreeProcessMemory - start psp:{ConvertUtils.ToHex16(pspSegment)}");
        if (!FreeProcessMemoryChain(_start, pspSegment, true)) {
            LogMemoryGraphic($"FreeProcessMemory - failed corrupted psp:{ConvertUtils.ToHex16(pspSegment)}");
            return false;
        }
        if (_umbChainStart is not null && UmbChainState == 0 &&
            !FreeProcessMemoryChain(_umbChainStart, pspSegment, true)) {
            LogMemoryGraphic($"FreeProcessMemory - failed corrupted psp:{ConvertUtils.ToHex16(pspSegment)}");
            return false;
        }

        LogMemoryGraphic($"FreeProcessMemory - done psp:{ConvertUtils.ToHex16(pspSegment)}");
        return true;
    }

    private bool FreeProcessMemoryChain(DosMemoryControlBlock? current, ushort pspSegment, bool compressFreeBlocks) {
        DosMemoryControlBlock? chainStart = current;
        while (current is not null) {
            if (!current.IsValid) {
                if (_loggerService.IsEnabled(LogLevel.Error)) {
                    _loggerService.LogError("MCB chain corrupted while freeing process memory");
                }
                return false;
            }
            if (current.PspSegment == pspSegment) {
                current.SetFree();
            }
            if (current.IsLast) {
                break;
            }
            current = current.GetNextOrDefault();
        }

        if (!compressFreeBlocks) {
            return true;
        }

        // Free all process blocks before coalescing so adjacent blocks owned by the same process
        // are merged even when the chain was traversed from low to high addresses.
        current = chainStart;
        while (current is not null) {
            if (!current.IsValid) {
                if (_loggerService.IsEnabled(LogLevel.Error)) {
                    _loggerService.LogError("MCB chain corrupted while coalescing process memory");
                }
                return false;
            }
            if (current.IsFree && !JoinBlocks(current, true)) {
                return false;
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

        LogMemoryGraphic($"FreeEnvironmentBlock - start env:{ConvertUtils.ToHex16(environmentSegment)} owner:{ConvertUtils.ToHex16(ownerPspSegment)}");

        ushort mcbSegment = (ushort)(environmentSegment - 1);
        DosMemoryControlBlock block = GetDosMemoryControlBlockFromSegment(mcbSegment);
        if (!CheckValidOrLogError(block)) {
            LogMemoryGraphic($"FreeEnvironmentBlock - invalid mcb env:{ConvertUtils.ToHex16(environmentSegment)}");
            return false;
        }

        if (block.PspSegment != ownerPspSegment) {
            if (_loggerService.IsEnabled(LogLevel.Trace)) {
                _loggerService.LogTrace(
                    "Environment block at {EnvSegment:X4} not owned by PSP {Owner:X4}, skipping free",
                    environmentSegment, ownerPspSegment);
            }
            LogMemoryGraphic($"FreeEnvironmentBlock - skipped not owner env:{ConvertUtils.ToHex16(environmentSegment)} owner:{ConvertUtils.ToHex16(ownerPspSegment)}");
            return true;
        }

        block.SetFree();
        JoinBlocks(_start, true);
        LogMemoryGraphic($"FreeEnvironmentBlock - freed env:{ConvertUtils.ToHex16(environmentSegment)} owner:{ConvertUtils.ToHex16(ownerPspSegment)}");
        return true;
    }
}
