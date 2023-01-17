namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Simple memory allocator for reserving regions of conventional memory.
/// </summary>
public class MetaAllocator {
    private readonly LinkedList<Allocation> allocations = new();

    public MetaAllocator() => Clear();

    /// <summary>
    /// Clears all allocations and resets the allocator to its initial state.
    /// </summary>
    public void Clear() {
        allocations.Clear();
        allocations.AddLast(new Allocation(0, Memory.ConvMemorySize >> 4, false));
    }

    /// <summary>
    /// Reserves a new block of memory.
    /// </summary>
    /// <param name="minimumSegment">Minimum requested segment to return.</param>
    /// <param name="bytes">Number of bytes requested.</param>
    /// <returns>Starting segment of the requested block of memory.</returns>
    public ushort Allocate(ushort minimumSegment, int bytes) {
        if (bytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        uint paragraphs = (uint)(bytes >> 4);
        if (bytes % 16 != 0) {
            paragraphs++;
        }

        Allocation freeBlock;
        try {
            freeBlock = allocations.First(a => !a.IsUsed && InRange(a, minimumSegment, paragraphs));
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException("Not enough conventional memory.", ex);
        }

        if (freeBlock.Length == paragraphs) {
            freeBlock.IsUsed = true;
            return freeBlock.Segment;
        }

        ushort providedSegment = Math.Max(minimumSegment, freeBlock.Segment);

        var newFreeBlockA = new Allocation(freeBlock.Segment, providedSegment - (uint)freeBlock.Segment, false);
        var newUsedBlock = new Allocation(providedSegment, paragraphs, true);
        var newFreeBlockB = new Allocation((ushort)(providedSegment + paragraphs), freeBlock.Length - newFreeBlockA.Length - paragraphs, false);

        var newBlocks = new List<Allocation>(3);
        if (newFreeBlockA.Length > 0) {
            newBlocks.Add(newFreeBlockA);
        }

        newBlocks.Add(newUsedBlock);
        if (newFreeBlockB.Length > 0) {
            newBlocks.Add(newFreeBlockB);
        }

        allocations.Replace(freeBlock, newBlocks.ToArray());

        return newUsedBlock.Segment;
    }

    /// <summary>
    /// Returns the size of the largest free block of memory.
    /// </summary>
    /// <returns>Size in bytes of the largest free block of memory.</returns>
    public uint GetLargestFreeBlockSize() {
        return allocations.Where(a => !a.IsUsed).Max(a => a.Length) << 4;
    }

    /// <summary>
    /// Returns a value indicating whether an allocation contains an address range.
    /// </summary>
    /// <param name="a">Allocation to test.</param>
    /// <param name="segment">Minimum requested segment address.</param>
    /// <param name="length">Requested allocation length.</param>
    /// <returns>True if allocation is acceptable; otherwise false.</returns>
    private static bool InRange(Allocation a, ushort segment, uint length) {
        if (a.Segment + a.Length >= segment + length) {
            return true;
        }

        if (a.Segment >= segment && a.Length >= length) {
            return true;
        }

        return false;
    }
}
