namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Structure used by XMS Function 0Bh (Move Extended Memory Block) to specify memory transfer details.<br/>
/// When SourceHandle or DestHandle is 0000h, their respective offset is treated as a segment:offset pair.<br/>
/// See <see href="https://fd.lod.bz/rbil/interrup/xms/0B.html"/>
/// </summary>
public sealed class ExtendedMemoryMoveStructure : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance of the Extended Memory Move Structure.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">Physical address where the structure is located (DS:SI).</param>
    public ExtendedMemoryMoveStructure(IByteReaderWriter byteReaderWriter, uint baseAddress) 
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the number of bytes to transfer.
    /// Must be even. WORD-aligned moves are faster, DWORD-aligned moves are fastest on 386+.
    /// </summary>
    public uint Length { get => UInt32[0x0]; set => UInt32[0x0] = value; }

    /// <summary>
    /// Gets or sets the handle of source memory block.
    /// If zero, SourceOffset is treated as segment:offset pair.
    /// </summary>
    public ushort SourceHandle { get => UInt16[0x4]; set => UInt16[0x4] = value; }

    /// <summary>
    /// Gets or sets the 32-bit offset into source block.
    /// If SourceHandle is zero, this is treated as segment:offset pair.
    /// </summary>
    public uint SourceOffset { get => UInt32[0x6]; set => UInt32[0x6] = value; }

    /// <summary>
    /// Gets or sets the handle of destination memory block.
    /// If zero, DestOffset is treated as segment:offset pair.
    /// </summary>
    public ushort DestHandle { get => UInt16[0xA]; set => UInt16[0xA] = value; }

    /// <summary>
    /// Gets or sets the 32-bit offset into destination block.
    /// If DestHandle is zero, this is treated as segment:offset pair.
    /// </summary>
    public uint DestOffset { get => UInt32[0xC]; set => UInt32[0xC] = value; }

    /// <summary>
    /// Gets the source address as a segment:offset value when SourceHandle is zero.
    /// </summary>
    public SegmentedAddress SourceAddress => new((ushort)(SourceOffset >> 16), (ushort)SourceOffset);

    /// <summary>
    /// Gets the destination address as a segment:offset value when DestHandle is zero.
    /// </summary>
    public SegmentedAddress DestAddress => new((ushort)(DestOffset >> 16), (ushort)DestOffset);
}