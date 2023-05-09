namespace Spice86.Core.Emulator.Memory;

using Spice86.Shared.Utils;

/// <summary>
/// Provides indexed byte access over memory.
/// </summary>
public class UInt8Indexer {
    private readonly Memory _memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UInt8Indexer"/> class with the specified memory.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public UInt8Indexer(Memory memory) => _memory = memory;

    /// <summary>
    /// Gets or sets the 8-bit unsigned integer at the specified index in the memory.
    /// </summary>
    /// <param name="i">The index of the element to get or set.</param>
    /// <returns>The 8-bit unsigned integer at the specified index in the memory.</returns>
    public byte this[uint i] {
        get => _memory.GetUint8(i);
        set => _memory.SetUint8(i, value);
    }

    /// <summary>
    /// Gets or sets the 8-bit unsigned integer at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    /// <returns>The 8-bit unsigned integer at the specified segment and offset in the memory.</returns>
    public byte this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }
}