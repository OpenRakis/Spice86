namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// Offset-only segmented memory indexer bound to the live SS register. Every access goes through
/// the three-argument memory indexer with <see cref="SegmentAccessKind.Stack"/>, so a segment-limit
/// fault raises #SS exactly like the CPU node the printed access comes from.
/// </summary>
/// <typeparam name="T">The element type the underlying memory indexer serves.</typeparam>
public sealed class SsIndexer<T>(MemoryIndexer<T> indexer, SegmentRegisters segmentRegisters) {
    private ushort Ss => segmentRegisters.UInt16[(uint)SegmentRegisterIndex.SsIndex];

    /// <summary>Gets or sets the value at <c>SS:offset</c> as a stack access.</summary>
    /// <param name="offset">The offset in the stack segment.</param>
    public T this[uint offset] {
        get => indexer[Ss, offset, SegmentAccessKind.Stack];
        set => indexer[Ss, offset, SegmentAccessKind.Stack] = value;
    }
}
