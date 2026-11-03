namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes data to emulated memory bus sequentially.
/// Keeps track of where to write next via CurrentAddress field that is automatically incremented each write.
/// </summary>
public class MemoryWriter {
    private readonly IIndexable _memory;
    private readonly List<(uint Start, uint End)> _reservedRanges = new();
    /// <summary>
    /// Where next data will be written
    /// </summary>
    public SegmentedAddress CurrentAddress { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryWriter"/> class with the specified memory as a data sink and beginningAddress for position for the first write.
    /// </summary>
    /// <param name="memory">memory bus to write to</param>
    /// <param name="beginningAddress">Address at which first write will be performed</param>
    public MemoryWriter(IIndexable memory, SegmentedAddress beginningAddress) {
        _memory = memory;
        CurrentAddress = beginningAddress;
    }

    /// <summary>
    /// Protects a fixed physical-memory region from sequential routine installation.
    /// Reservations must be added before installing routines.
    /// </summary>
    /// <param name="startAddress">The first reserved physical address.</param>
    /// <param name="length">The number of reserved bytes.</param>
    public void ReserveRange(uint startAddress, uint length) {
        if (length == 0 || startAddress > uint.MaxValue - length) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        _reservedRanges.Add((startAddress, startAddress + length));
    }

    private void CheckWritable(uint length) {
        uint address = CurrentAddress.Linear;
        foreach ((uint start, uint end) in _reservedRanges) {
            if (address < end && address + length > start) {
                throw new InvalidOperationException($"Assembly routine at {CurrentAddress} overlaps reserved firmware memory.");
            }
        }
    }

    /// <summary>
    /// Writes the given byte at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next byte.
    /// </summary>
    /// <param name="b">data to write</param>
    public void WriteUInt8(byte b) {
        CheckWritable(1);
        _memory.UInt8[CurrentAddress.Segment, CurrentAddress.Offset] = b;
        CurrentAddress += 1;
    }

    /// <summary>
    /// Writes the given sbyte at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next byte.
    /// </summary>
    /// <param name="b">data to write</param>
    public void WriteInt8(sbyte b) {
        CheckWritable(1);
        _memory.Int8[CurrentAddress.Segment, CurrentAddress.Offset] = b;
        CurrentAddress += 1;
    }

    /// <summary>
    /// Writes the given word at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next word.
    /// </summary>
    /// <param name="w">data to write</param>
    public void WriteUInt16(ushort w) {
        CheckWritable(2);
        _memory.UInt16[CurrentAddress.Segment, CurrentAddress.Offset] = w;
        CurrentAddress += 2;
    }

    /// <summary>
    /// Writes the given signed word at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next word.
    /// </summary>
    /// <param name="w">data to write</param>
    public void WriteInt16(short w) {
        CheckWritable(2);
        _memory.Int16[CurrentAddress.Segment, CurrentAddress.Offset] = w;
        CurrentAddress += 2;
    }

    /// <summary>
    /// Writes the given dword at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next dword.
    /// </summary>
    /// <param name="dw">data to write</param>
    public void WriteUInt32(uint dw) {
        CheckWritable(4);
        _memory.UInt32[CurrentAddress.Segment, CurrentAddress.Offset] = dw;
        CurrentAddress += 4;
    }

    /// <summary>
    /// Writes the given Segmented Address at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next position.
    /// Offset is written first then Segment.
    /// </summary>
    /// <param name="address">data to write</param>
    public void WriteSegmentedAddress(SegmentedAddress address) {
        CheckWritable(4);
        _memory.SegmentedAddress16[CurrentAddress.Segment, CurrentAddress.Offset] = address;
        CurrentAddress += 4;
    }

}