namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes data to emulated memory bus sequentially.
/// Keeps track of where to write next via CurrentAddress field that is automatically incremented each write.
/// </summary>
public class MemoryWriter {
    private readonly IIndexable _memory;
    /// <summary>
    /// Where next data will be written
    /// </summary>
    public SegmentedAddress CurrentAddress { get; set; }

    /// <summary>
    /// Creates and returns a copy of CurrentAddress so that the returned instance is not impacted by changes to CurrentAddress.
    /// </summary>
    /// <returns>a copy of CurrentAddress</returns>
    public SegmentedAddress GetCurrentAddressCopy() {
        return new SegmentedAddress(CurrentAddress);
    }

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
    /// Writes the given byte at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next byte.
    /// </summary>
    /// <param name="b">data to write</param>
    public void WriteUInt8(byte b) {
        _memory.UInt8[CurrentAddress.Segment, CurrentAddress.Offset] = b;
        CurrentAddress += 1;
    }

    /// <summary>
    /// Writes the given word at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next word.
    /// </summary>
    /// <param name="w">data to write</param>
    public void WriteUInt16(ushort w) {
        _memory.UInt16[CurrentAddress.Segment, CurrentAddress.Offset] = w;
        CurrentAddress += 2;
    }

    /// <summary>
    /// Writes the given dword at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next dword.
    /// </summary>
    /// <param name="dw">data to write</param>
    public void WriteUInt32(uint dw) {
        _memory.UInt32[CurrentAddress.Segment, CurrentAddress.Offset] = dw;
        CurrentAddress += 4;
    }

    /// <summary>
    /// Writes the given Segmented Address at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next position.
    /// Offset is written first then Segment.
    /// </summary>
    /// <param name="address">data to write</param>
    public void WriteSegmentedAddress(SegmentedAddress address) {
        _memory.SegmentedAddress[CurrentAddress.Segment, CurrentAddress.Offset] = address;
        CurrentAddress += 4;
    }

}