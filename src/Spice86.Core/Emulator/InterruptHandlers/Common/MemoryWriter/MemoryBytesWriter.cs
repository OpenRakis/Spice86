namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes data to emulated memory bus sequentially.
/// Keeps track of where to write next via CurrentAddress field that is automatically incremented each write. 
/// </summary>
public class MemoryBytesWriter {
    private readonly Memory _memory;
    /// <summary>
    /// Where next data will be written
    /// </summary>
    public SegmentedAddress CurrentAddress { get; set; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="memory">memory bus to write to</param>
    /// <param name="beginningAddress">Address at which first write will be performed</param>
    public MemoryBytesWriter(Memory memory, SegmentedAddress beginningAddress) {
        _memory = memory;
        CurrentAddress = beginningAddress;
    }

    /// <summary>
    /// Writes the given byte at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next byte.
    /// </summary>
    /// <param name="b">data to write</param>
    public void WriteByte(byte b) {
        _memory.UInt8[CurrentAddress.Segment, CurrentAddress.Offset] = b;
        CurrentAddress.Offset++;
    }

    /// <summary>
    /// Writes the given byte at CurrentAddress to emulated memory bus, increments CurrentAddress offset to next word.
    /// </summary>
    /// <param name="w">data to write</param>
    public void WriteWord(ushort w) {
        _memory.UInt16[CurrentAddress.Segment, CurrentAddress.Offset] = w;
        CurrentAddress.Offset += 2;
    }
}