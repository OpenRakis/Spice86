namespace Spice86.Core.Emulator.Memory;

using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed unsigned 16-byte access over memory.
/// </summary>
public class UInt16Indexer {
    private readonly Memory _memory;

    /// <summary>
    /// Creates a new instance of the <see cref="UInt16Indexer"/> class
    /// with the specified <see cref="Memory"/> instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public UInt16Indexer(Memory memory) => _memory = memory;

    /// <summary>
    /// Gets or sets the unsigned 16-bit integer at the specified physical address.
    /// </summary>
    /// <param name="address">The physical address of the value to get or set.</param>
    /// <returns>The unsigned 16-bit integer at the specified physical address.</returns>
    public ushort this[uint address] {
        get => _memory.GetUint16(address);
        set => _memory.SetUint16(address, value);
    }

    /// <summary>
    /// Gets or sets the unsigned 16-bit integer at the specified segment/offset pair.
    /// </summary>
    /// <param name="segment">The segment of the value to get or set.</param>
    /// <param name="offset">The offset of the value to get or set.</param>
    /// <returns>The unsigned 16-bit integer at the specified segment/offset pair.</returns>
    public ushort this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }
}