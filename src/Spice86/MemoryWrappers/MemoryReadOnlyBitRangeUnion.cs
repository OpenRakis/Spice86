namespace Spice86.MemoryWrappers;

using AvaloniaHex.Document;

using Spice86.Core.Emulator.Memory;

using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

public class MemoryReadOnlyBitRangeUnion : IReadOnlyBitRangeUnion {
    private readonly IMemory _memory;

    public MemoryReadOnlyBitRangeUnion(IMemory memory) {
        _memory = memory;
        EnclosingRange = new BitRange(0, _memory.Length * 8);
    }

    public BitRange EnclosingRange { get; }
    public int Count => (int)_memory.Length;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public bool Contains(BitLocation location) {
        return location.CompareTo(EnclosingRange.Start) >= 0 && location.CompareTo(EnclosingRange.End) < 0;
    }

    public BitRangeUnion.Enumerator GetEnumerator() {
        var bitRangeUnion = new BitRangeUnion();
        // Multiply by 8 to convert from bytes to bits
        bitRangeUnion.Add(new BitRange(0, _memory.Length * 8));
        return bitRangeUnion.GetEnumerator();
    }

    public bool IntersectsWith(BitRange range) {
        return EnclosingRange.Start < range.End && EnclosingRange.End > range.Start;
    }

    public bool IsSuperSetOf(BitRange range) {
        return EnclosingRange.Start <= range.Start && EnclosingRange.End >= range.End;
    }

    IEnumerator<BitRange> IEnumerable<BitRange>.GetEnumerator() {
        for (uint i = 0; i < _memory.Length; i++) {
            yield return EnclosingRange;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable<BitRange>)this).GetEnumerator();
    }
}
