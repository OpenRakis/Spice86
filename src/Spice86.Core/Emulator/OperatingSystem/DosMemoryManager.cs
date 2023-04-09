namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

public class DosMemoryManager {
    private readonly ILoggerService _loggerService;
    private readonly Memory _memory;
    private ushort _pspSegment;
    private DosMemoryControlBlock? _start;

    public DosMemoryManager(Memory memory, ILoggerService loggerService) {
        _loggerService = loggerService;
        _memory = memory;
    }

    public DosMemoryControlBlock? AllocateMemoryBlock(ushort requestedSize) {
        IEnumerable<DosMemoryControlBlock> candidates = FindCandidatesForAllocation(requestedSize);

        // take the smallest
        DosMemoryControlBlock? blockOptional = null;
        foreach (DosMemoryControlBlock currentElement in candidates) {
            if (blockOptional is null || currentElement.Size < blockOptional.Size) {
                blockOptional = currentElement;
            }
        }
        if (blockOptional is null) {
            // Nothing found
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("Could not find any MCB to fit {RequestedSize}.", requestedSize);
            }
            return null;
        }

        DosMemoryControlBlock block = blockOptional;
        if (!SplitBlock(block, requestedSize)) {
            // An issue occurred while splitting the block
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("Could not spit block {Block}.", block);
            }
            return null;
        }

        block.PspSegment = _pspSegment;
        return block;
    }

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

            current = current?.Next();
        }
    }

    public bool FreeMemoryBlock(ushort blockSegment) {
        DosMemoryControlBlock block = GetDosMemoryControlBlockFromSegment(blockSegment);
        if (!CheckValidOrLogError(block)) {
            return false;
        }

        block.SetFree();
        return JoinBlocks(block, true);
    }

    public ushort PspSegment => _pspSegment;

    public void Init(ushort pspSegment, ushort lastFreeSegment) {
        ushort startSegment = (ushort)(pspSegment - 1);
        _pspSegment = pspSegment;
        ushort size = (ushort)(lastFreeSegment - startSegment);
        _start = GetDosMemoryControlBlockFromSegment(startSegment);

        // size -1 because the mcb itself takes 16 bytes which is 1 paragraph
        _start.Size = (ushort)(size - 1);
        _start.SetFree();
        _start.SetLast();
    }

    public bool ModifyBlock(ushort blockSegment, ushort requestedSize) {
        DosMemoryControlBlock block = GetDosMemoryControlBlockFromSegment(blockSegment);
        if (!CheckValidOrLogError(block)) {
            return false;
        }

        // Make the block the biggest it can get
        if (!JoinBlocks(block, false)) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("Could not join MCB {Block}.", block);
            }
            return false;
        }

        if (block.Size < requestedSize - 1) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is too small for requested size {RequestedSize}.", block, requestedSize);
            }
            return false;
        }

        if (block.Size > requestedSize) {
            SplitBlock(block, requestedSize);
        }

        block.PspSegment = _pspSegment;
        return true;
    }

    private bool CheckValidOrLogError(DosMemoryControlBlock? block) {
        if (block is null || !block.IsValid) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("MCB {Block} is invalid.", block);
            }
            return false;
        }

        return true;
    }

    private IEnumerable<DosMemoryControlBlock> FindCandidatesForAllocation(int requestedSize) {
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
            current = current?.Next();
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
            DosMemoryControlBlock next = block.Next();
            if (!next.IsFree) {
                // end of the free blocks reached
                break;
            }

            if (!CheckValidOrLogError(next)) {
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error("MCB {NextBlock} is not valid.", next);
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
    /// <param name="block"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    private bool SplitBlock(DosMemoryControlBlock block, ushort size) {
        ushort blockSize = block.Size;
        if (blockSize == size) {
            // nothing to do
            return true;
        }

        int nextBlockSize = blockSize - size - 1;
        if (nextBlockSize < 0) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("Cannot split block {Block} with size {Size} because it is too small.", block, size);
            }
            return false;
        }

        block.Size = size;
        DosMemoryControlBlock next = block.Next();

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