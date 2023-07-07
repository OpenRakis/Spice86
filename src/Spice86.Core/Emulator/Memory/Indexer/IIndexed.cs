namespace Spice86.Core.Emulator.Memory; 

/// <summary>
/// Interface for objects that allow access to their data as byte, ushort, or uint
/// </summary>
public interface IIndexed {
    /// <summary>
    ///     Allows indexed byte access to the memory map.
    /// </summary>
    public UInt8Indexer UInt8 {
        get;
    }

    /// <summary>
    ///     Allows indexed word access to the memory map.
    /// </summary>
    public UInt16Indexer UInt16 {
        get;
    }

    /// <summary>
    ///     Allows indexed double word access to the memory map.
    /// </summary>
    public UInt32Indexer UInt32 {
        get;
    }
}