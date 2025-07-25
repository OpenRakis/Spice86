namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Implements DOS memory operations, such as allocating and releasing MCBs.
/// <remarks>
/// Finding a free MCB involves walking the MCB chains, with compression done first (compression joins MCBs together).
/// </remarks>
/// </summary>
public class DosMemoryManager {
    internal const ushort LastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 2;
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;
    private readonly DosProcessManager _processManager;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="processManager">The class responsible to launch DOS programs and take care of the DOS PSP chain.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosMemoryManager(IMemory memory,
        DosProcessManager processManager, ILoggerService loggerService) {
        _loggerService = loggerService;
        _processManager = processManager;
        _memory = memory;



        DosMemoryControlBlock baseMcb = GetDosMemoryControlBlockFromSegment((ushort)(DosSysVars.FirstMcbSegment));
        baseMcb.AllocationSize = LastFreeSegment - DosSysVars.FirstMcbSegment - 1;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information(
                "DOS available memory: {ConventionalFree} - in paragraphs: {DosFreeParagraphs}",
                baseMcb.AllocationSizeInBytes, baseMcb.AllocationSize);
        }
        baseMcb.SetFree();
        baseMcb.SetLast();
    }

    /// <summary>
    /// Allocates a memory block of the specified size. Returns <c>null</c> if no memory block could be found to fit the requested size.
    /// </summary>
    /// <param name="requestedSize">The requested size of the memory block.</param>
    /// <returns>The allocated <see cref="DosMemoryControlBlock"/> or <c>null</c> if no memory block could be found.</returns>
    public DosMemoryControlBlock? AllocateMemoryBlock(ushort requestedSize) {
        CompressMemory();
        ushort biggestSize = 0;
        ushort mcbSegment = DosSysVars.FirstMcbSegment;

        while (true) {
            DosMemoryControlBlock mcb = GetDosMemoryControlBlockFromSegment(mcbSegment);
            if (!CheckValidOrLogError(mcb)) {
                return null;
            }

            if (mcb.IsFree) {
                ushort blockSize = mcb.AllocationSize;
                if (blockSize < requestedSize) {
                    if (biggestSize < blockSize) {
                        biggestSize = blockSize;
                    }
                } else if (blockSize == requestedSize) {
                    mcb.PspSegment = _processManager.GetCurrentPspSegment();
                    return mcb;
                } else {
                    // Split block
                    DosMemoryControlBlock mcbNext = GetDosMemoryControlBlockFromSegment((ushort)(mcbSegment + requestedSize + 1));
                    mcbNext.PspSegment = DosMemoryControlBlock.FreeMcbMarker;
                    mcbNext.TypeField = mcb.TypeField;
                    mcbNext.AllocationSize = (ushort)(blockSize - requestedSize - 1);
                    mcb.AllocationSize = requestedSize;
                    mcb.SetNonLast();
                    mcb.PspSegment = _processManager.GetCurrentPspSegment();
                    return mcb;
                }
            }

            if (mcb.IsLast) {
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error("Could not find any MCB to fit {RequestedSize}. Largest available: {BiggestSize}",
                        requestedSize, biggestSize);
                }
                return null;
            }
            mcbSegment = (ushort)(mcbSegment + mcb.AllocationSize + 1);
        }
    }

    /// <summary>
    /// Compacts adjacent free DOS memory control blocks into a single block.
    /// </summary>
    private void CompressMemory() {
        ushort mcbSegment = DosSysVars.FirstMcbSegment;
        DosMemoryControlBlock mcb = GetDosMemoryControlBlockFromSegment(mcbSegment);

        while (mcb.IsNonLast) {
            DosMemoryControlBlock mcbNext = GetDosMemoryControlBlockFromSegment(
                (ushort)(mcbSegment + mcb.AllocationSize + 1));
            if (mcb.PspSegment == 0 && mcbNext.PspSegment == 0 && mcbNext.IsValid) {
                mcb.AllocationSize = (ushort)(mcb.AllocationSize + mcbNext.AllocationSize + 1);
                mcb.TypeField = mcbNext.TypeField;
            } else {
                mcbSegment = (ushort)(mcbSegment + mcb.AllocationSize + 1);
                mcb = GetDosMemoryControlBlockFromSegment(mcbSegment);
            }
        }
    }

    /// <summary>
    /// Finds the largest free <see cref="DosMemoryControlBlock"/>.
    /// </summary>
    /// <returns>The largest free <see cref="DosMemoryControlBlock"/></returns>
    public DosMemoryControlBlock FindLargestFree() {
        ushort mcbSegment = DosSysVars.FirstMcbSegment;
        DosMemoryControlBlock? largest = null;

        while (true) {
            DosMemoryControlBlock mcb = GetDosMemoryControlBlockFromSegment(mcbSegment);
            if (mcb.IsFree && (largest == null || mcb.AllocationSize > largest.AllocationSize)) {
                largest = mcb;
            }
            if (mcb.IsLast) {
                return largest ?? mcb;
            }
            mcbSegment = (ushort)(mcbSegment + mcb.AllocationSize + 1);
        }
    }

    /// <summary>
    /// Releases an MCB.
    /// </summary>
    /// <param name="blockSegment">The segment number of the MCB.</param>
    /// <returns>Whether the operation was successful.</returns>
    public bool FreeMemoryBlock(ushort blockSegment) {
        DosMemoryControlBlock mcb = GetDosMemoryControlBlockFromSegment(blockSegment);
        if (!mcb.IsValid) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is invalid", mcb);
            }
            return false;
        }
        mcb.SetFree();
        return true;
    }

    /// <summary>
    /// Extends or reduces a MCB.
    /// </summary>
    /// <param name="blockSegment">The segment number of the MCB.</param>
    /// <param name="requestedSize">The new size for the MCB, in paragraphs.</param>
    /// <param name="dosMemoryControlBlock">The modified memory control block, or <c>null</c> if the operation was not successful.</param>
    /// <param name="errorCode">The DOS error code, either 0x7 (mcb destroyed) or 0x8 (not enough memory). <c>null</c> if the operation was successful.</param>
    /// <returns>Whether the operation was successful.</returns>
    public bool TryModifyBlock(ushort blockSegment, ref ushort requestedSize,
        [NotNullWhen(true)] out DosMemoryControlBlock? dosMemoryControlBlock,
        [NotNullWhen(false)] out ErrorCode? errorCode) {
        dosMemoryControlBlock = null;
        if (blockSegment < DosSysVars.FirstMcbSegment + 1) {
            errorCode = ErrorCode.MemoryControlBlockDestroyed;
            return false;
        }

        DosMemoryControlBlock mcb = GetDosMemoryControlBlockFromSegment((ushort)(blockSegment - 1));
        if (!mcb.IsValid) {
            errorCode = ErrorCode.MemoryControlBlockDestroyed;
            return false;
        }

        CompressMemory();
        ushort total = mcb.AllocationSize;
        DosMemoryControlBlock mcbNext = GetDosMemoryControlBlockFromSegment((ushort)(blockSegment + total));

        if (requestedSize <= total) {
            if (requestedSize == total) {
                dosMemoryControlBlock = mcb;
                mcb.PspSegment = _processManager.GetCurrentPspSegment();
                errorCode = null;
                return true;
            }
            // Shrinking MCB
            DosMemoryControlBlock mcbNewNext = GetDosMemoryControlBlockFromSegment((ushort)(blockSegment + requestedSize));
            mcb.AllocationSize = requestedSize;
            mcbNewNext.TypeField = mcb.TypeField;
            if (mcb.IsLast) {
                mcb.SetNonLast();
            }
            mcbNewNext.AllocationSize = (ushort)(total - requestedSize - 1);
            mcbNewNext.SetFree();
            dosMemoryControlBlock = mcb;
            errorCode = null;
            return true;
        }

        // MCB will grow, try to join with following MCB
        if (mcb.IsNonLast && mcbNext.IsFree) {
            total = (ushort)(total + mcbNext.AllocationSize + 1);
        }
        if (requestedSize < total) {
            if (mcb.IsNonLast && mcbNext.IsFree) {
                mcb.TypeField = mcbNext.TypeField;
            }
            mcb.AllocationSize = requestedSize;
            mcbNext = GetDosMemoryControlBlockFromSegment((ushort)(blockSegment + requestedSize));
            mcbNext.AllocationSize = (ushort)(total - requestedSize - 1);
            mcbNext.TypeField = mcb.TypeField;
            mcbNext.SetFree();
            mcb.SetNonLast();
            dosMemoryControlBlock = mcb;
            errorCode = null;
            return true;
        }

        // At this point: requestedSize==total (fits) or requestedSize>total,
        // in the second case resize block to maximum
        if (mcb.IsNonLast && mcbNext.IsFree) {
            mcb.TypeField = mcbNext.TypeField;
        }
        mcb.AllocationSize = total;
        mcb.PspSegment = _processManager.GetCurrentPspSegment();
        if (requestedSize == total) {
            dosMemoryControlBlock = mcb;
            errorCode = null;
            return true;
        }
        requestedSize = total;
        errorCode = ErrorCode.InsufficientMemory;
        return false;
    }

    private bool CheckValidOrLogError(DosMemoryControlBlock? block) {
        if (block is null || !block.IsValid) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is invalid", block);
            }
            return false;
        }
        return true;
    }

    private DosMemoryControlBlock GetDosMemoryControlBlockFromSegment(ushort blockSegment) {
        return new DosMemoryControlBlock(_memory, MemoryUtils.ToPhysicalAddress(blockSegment, 0));
    }
}