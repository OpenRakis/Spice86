namespace Spice86.ViewModels.DataModels;

using AvaloniaHex.Document;

using JetBrains.Annotations;

using System.Collections;
using System.Collections.Specialized;

/// <inheritdoc/>
internal class MemoryReadOnlyBitRangeUnion : IReadOnlyBitRangeUnion {
    private readonly uint _startAddress;
    private readonly uint _endAddress;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryReadOnlyBitRangeUnion"/> class.
    /// </summary>
    /// <param name="startAddress">The start address of tha range of memory.</param>
    /// <param name="endAddress">The end address of the range of memory.</param>
    public MemoryReadOnlyBitRangeUnion(uint startAddress, uint endAddress) {
        _startAddress = startAddress;
        _endAddress = endAddress;
        // The endAddress is excluded from the range
        EnclosingRange = new BitRange(startAddress, endAddress);
    }

    public BitRange EnclosingRange { get; }
    public int Count => (int)(_endAddress - _startAddress);

    public bool IsFragmented => false;

#pragma warning disable CS0067 // Event intentionally unused - read-only union never raises changes
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
#pragma warning restore CS0067

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
    public int GetOverlappingRanges(BitRange range, Span<BitRange> output) {
        // For a non-fragmented union, if there's any overlap, return the entire range
        if (IntersectsWith(range)) {
            if (output.Length > 0) {
                output[0] = EnclosingRange;
            }
            return 1;
        }
        return 0;
    }

    public int GetIntersectingRanges(BitRange range, Span<BitRange> output) {
        // Return the actual intersection, not the entire range
        if (IntersectsWith(range)) {
            if (output.Length > 0) {
                // Calculate the actual intersection
                BitLocation start = EnclosingRange.Start > range.Start ? EnclosingRange.Start : range.Start;
                BitLocation end = EnclosingRange.End < range.End ? EnclosingRange.End : range.End;
                output[0] = new BitRange(start, end);
            }
            return 1;
        }
        return 0;
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