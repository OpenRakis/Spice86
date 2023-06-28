namespace Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Interface for objects that allow access to their data as byte, ushort, or uint
/// </summary>
public interface IIndexed {
    /// <summary>
    ///     Allows indexed byte access to the memory.
    /// </summary>
    public UInt8Indexer UInt8 {
        get;
    }

    /// <summary>
    ///     Allows indexed word access to the memory.
    /// </summary>
    public UInt16Indexer UInt16 {
        get;
    }

    /// <summary>
    ///     Allows indexed double word access to the memory.
    /// </summary>
    public UInt32Indexer UInt32 {
        get;
    }

    /// <summary>
    ///     Allows indexed 16 bit Offset / Segment access to the memory.
    /// </summary>
    public OffsetSegmentIndexer OffsetSegment {
        get;
    }
}