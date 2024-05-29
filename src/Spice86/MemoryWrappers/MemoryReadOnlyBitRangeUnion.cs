namespace Spice86.MemoryWrappers;

using AvaloniaHex.Document;

using Spice86.Core.Emulator.Memory;

using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

public class MemoryReadOnlyBitRangeUnion : IReadOnlyBitRangeUnion {
    private readonly IMemory _memory;
    private readonly uint _startAddress;
    private readonly uint _endAddress;

    public MemoryReadOnlyBitRangeUnion(IMemory memory, uint startAddress, uint endAddress) {
        _memory = memory;
        _startAddress = startAddress;
        _endAddress = endAddress;
        EnclosingRange = new BitRange(startAddress, endAddress);
    }

    public BitRange EnclosingRange { get; }
    public int Count => (int)_memory.Length;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public bool Contains(BitLocation location) {
        return location.CompareTo(EnclosingRange.Start) >= 0 && location.CompareTo(EnclosingRange.End) < 0;
    }

    public BitRangeUnion.Enumerator GetEnumerator() {
        var bitRangeUnion = new BitRangeUnion();
        bitRangeUnion.Add(new BitRange(_startAddress, _endAddress));
        return bitRangeUnion.GetEnumerator();
    }

    public bool IntersectsWith(BitRange range) {
        return EnclosingRange.Start < range.End && EnclosingRange.End > range.Start;
    }

    public bool IsSuperSetOf(BitRange range) {
        return EnclosingRange.Start <= range.Start && EnclosingRange.End >= range.End;
    }

    IEnumerator<BitRange> IEnumerable<BitRange>.GetEnumerator() {
        for (uint i = _startAddress; i < _endAddress; i++) {
            yield return EnclosingRange;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable<BitRange>)this).GetEnumerator();
    }
}
