namespace Spice86.MemoryWrappers;

using AvaloniaHex.Document;

using JetBrains.Annotations;

using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

/// <inheritdoc/>
internal class MemoryReadOnlyBitRangeUnion : IReadOnlyBitRangeUnion {
    private readonly uint _startAddress;
    private readonly uint _endAddress;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryReadOnlyBitRangeUnion"/> class.
    /// </summary>
    /// <param name="startAddress">The start address of tha range of memory.</param>
    /// <param name="endAddress">The end address of the range of memory. This end address is not included in the range.</param>
    public MemoryReadOnlyBitRangeUnion(uint startAddress, uint endAddress) {
        _startAddress = startAddress;
        _endAddress = endAddress;
        // The endAddress is excluded from the range
        EnclosingRange = new BitRange(startAddress, endAddress);
    }

    public BitRange EnclosingRange { get; }
    public int Count => (int)(_endAddress - _startAddress);

    public bool IsFragmented => false;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public bool Contains(BitLocation location) {
        return location.CompareTo(EnclosingRange.Start) >= 0 && location.CompareTo(EnclosingRange.End) < 0;
    }

    [MustDisposeResource]
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

    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable<BitRange>)this).GetEnumerator();
    }
}
