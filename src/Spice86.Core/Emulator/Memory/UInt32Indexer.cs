namespace Spice86.Core.Emulator.Memory;

using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed unsigned 32-bit access over memory.
/// </summary>
public class UInt32Indexer {
    private readonly Memory _memory;

    /// <summary>
    /// Initializes a new instance of the UInt32Indexer class with a specified memory bus.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public UInt32Indexer(Memory memory) => _memory = memory;

    /// <summary>
    /// Gets or sets the unsigned 32-bit value at the specified memory address.
    /// </summary>
    /// <param name="address">The memory address to access.</param>
    /// <returns>The unsigned 32-bit value at the specified memory address.</returns>
    public uint this[uint address] {
        get => _memory.GetUint32(address);
        set => _memory.SetUint32(address, value);
    }

    /// <summary>
    /// Gets or sets the unsigned 32-bit value at the specified segment and offset.
    /// </summary>
    /// <param name="segment">The segment to access.</param>
    /// <param name="offset">The offset within the segment to access.</param>
    /// <returns>The unsigned 32-bit value at the specified segment and offset.</returns>
    public uint this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }
}