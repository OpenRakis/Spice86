namespace Spice86.Emulator.InterruptHandlers.Dos;

using Serilog;

using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.Linq;

public class DosMemoryManager {
    private static readonly ILogger _logger = Log.Logger.ForContext<DosMemoryManager>();
    private Memory memory;
    private ushort pspSegment;
    private DosMemoryControlBlock start;

    public DosMemoryManager(Memory memory) {
        this.memory = memory;
    }

    public DosMemoryControlBlock? AllocateMemoryBlock(ushort requestedSize) {
        IList<DosMemoryControlBlock> candidates = FindCandidatesForAllocation(requestedSize);

        // take the smallest
        var blockOptional = candidates.OrderBy(x => x.GetSize()).FirstOrDefault();
        if (blockOptional is null) {
            // Nothing found
            _logger.Error("Could not find any MCB to fit {@RequestedSize}.", requestedSize);
            return null;
        }

        DosMemoryControlBlock block = blockOptional;
        if (!SplitBlock(block, requestedSize)) {
            // An issue occurred while splitting the block
            _logger.Error("Could not spit block {@Block}.", block);
            return null;
        }

        block.SetPspSegment(pspSegment);
        return block;
    }

    public DosMemoryControlBlock? FindLargestFree() {
        DosMemoryControlBlock? current = start;
        DosMemoryControlBlock? largest = null;
        while (true) {
            if (current != null && current.IsFree() && (largest == null || current.GetSize() > largest.GetSize())) {
                largest = current;
            }

            if (current != null && current.IsLast() && largest != null) {
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

    public ushort GetPspSegment() {
        return pspSegment;
    }

    public void Init(ushort pspSegment, ushort lastFreeSegment) {
        ushort startSegment = (ushort)(pspSegment - 1);
        this.pspSegment = pspSegment;
        ushort size = (ushort)(lastFreeSegment - startSegment);
        start = GetDosMemoryControlBlockFromSegment(startSegment);

        // size -1 because the mcb itself takes 16 bytes which is 1 paragraph
        start.SetSize((ushort)(size - 1));
        start.SetFree();
        start.SetLast();
    }

    public bool ModifyBlock(ushort blockSegment, ushort requestedSize) {
        DosMemoryControlBlock block = GetDosMemoryControlBlockFromSegment(blockSegment);
        if (!CheckValidOrLogError(block)) {
            return false;
        }

        // Make the block the biggest it can get
        if (!JoinBlocks(block, false)) {
            _logger.Error("Could not join MCB {@Block}.", block);
            return false;
        }

        if (block.GetSize() < requestedSize - 1) {
            _logger.Error("MCB {@Block} is too small for requested size {@RequestedSize}.", block, requestedSize);
            return false;
        }

        if (block.GetSize() > requestedSize) {
            SplitBlock(block, requestedSize);
        }

        block.SetPspSegment(pspSegment);
        return true;
    }

    private bool CheckValidOrLogError(DosMemoryControlBlock? block) {
        if (block is null || block.IsValid()) {
            _logger.Error("MCB {@Block} is invalid.", block);
            return false;
        }

        return true;
    }

    private IList<DosMemoryControlBlock> FindCandidatesForAllocation(int requestedSize) {
        DosMemoryControlBlock current = start;
        List<DosMemoryControlBlock> candidates = new();
        while (true) {
            if (!CheckValidOrLogError(current)) {
                return new List<DosMemoryControlBlock>();
            }
            JoinBlocks(current, true);
            if (current.IsFree() && current.GetSize() >= requestedSize) {
                candidates.Add(current);
            }
            if (current.IsLast()) {
                return candidates;
            }
            current = current.Next();
        }
    }

    private DosMemoryControlBlock GetDosMemoryControlBlockFromSegment(ushort blockSegment) {
        return new DosMemoryControlBlock(memory, MemoryUtils.ToPhysicalAddress(blockSegment, 0));
    }

    private bool JoinBlocks(DosMemoryControlBlock block, bool onlyIfFree) {
        if (onlyIfFree && !block.IsFree()) {
            // Do not touch blocks in use
            return true;
        }

        while (block.IsNonLast()) {
            DosMemoryControlBlock next = block.Next();
            if (!next.IsFree()) {
                // end of the free blocks reached
                break;
            }

            if (!CheckValidOrLogError(next)) {
                _logger.Error("MCB {@NextBlock} is not valid.", next);
                return false;
            }

            JoinContiguousBlocks(block, next);
        }

        return true;
    }

    private void JoinContiguousBlocks(DosMemoryControlBlock destination, DosMemoryControlBlock next) {
        destination.SetTypeField(next.GetTypeField());

        // +1 because next block metadata is going to free space
        destination.SetSize((ushort)(destination.GetSize() + next.GetSize() + 1));
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
        ushort blockSize = block.GetSize();
        if (blockSize == size) {
            // nothing to do
            return true;
        }

        int nextBlockSize = blockSize - size - 1;
        if (nextBlockSize < 0) {
            _logger.Error("Cannot split block {@Block} with size {@Size} because it is too small.", block, size);
            return false;
        }

        block.SetSize(size);
        DosMemoryControlBlock next = block.Next();

        // if it was last propagate it
        next.SetTypeField(block.GetTypeField());

        // we are non last now for sure
        block.SetNonLast();

        // next is free
        next.SetFree();
        next.SetSize((ushort)(nextBlockSize));
        return true;
    }
}