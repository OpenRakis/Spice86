namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents the DOS media ID table stored in the DOS private segment area.
/// Each of the 26 possible drives has a 9-byte entry; the first byte is the FAT
/// media descriptor returned by INT 21h AH=1Bh/1Ch via DS:BX.
/// </summary>
/// <remarks>
/// The 9-byte stride matches DOSBox's DPB layout (<c>mediaid = RealMake(dpb, 0x17)</c>)
/// where media IDs are spaced at <c>i*9</c> offsets.
/// </remarks>
public class DosMediaIdTable : MemoryBasedDataStructure {
    /// <summary>
    /// The in-segment DPB offset where DOS expects the media-id table to start.
    /// </summary>
    public const ushort EntryBaseOffsetInSegment = 0x17;

    /// <summary>Bytes per drive entry.</summary>
    public const int EntrySize = 9;

    /// <summary>Total bytes required for all 26 drive entries.</summary>
    public const int TableSizeInBytes = EntryBaseOffsetInSegment + (26 * EntrySize);

    /// <summary>Paragraphs (16-byte blocks) required to hold the full table.</summary>
    public const int TableSizeInParagraphs = (TableSizeInBytes + 15) / 16;

    /// <summary>The segment of this table in the DOS private area.</summary>
    public ushort Segment { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The physical base address of the table.</param>
    /// <param name="segment">The segment value used to build DS:BX pointers for callers.</param>
    public DosMediaIdTable(IByteReaderWriter byteReaderWriter, uint baseAddress, ushort segment)
        : base(byteReaderWriter, baseAddress) {
        Segment = segment;
    }

    /// <summary>Gets or sets the FAT media descriptor byte for the given zero-based drive index.</summary>
    public byte this[byte driveIndex] {
        get => UInt8[driveIndex * EntrySize];
        set => UInt8[driveIndex * EntrySize] = value;
    }

    /// <summary>
    /// Returns the in-segment offset of the given drive's entry, for use as BX in the DS:BX pointer.
    /// </summary>
    public ushort EntryOffset(byte driveIndex) => (ushort)(EntryBaseOffsetInSegment + (driveIndex * EntrySize));
}
