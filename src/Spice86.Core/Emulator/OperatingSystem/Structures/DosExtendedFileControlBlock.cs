namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents an Extended DOS File Control Block (XFCB) in memory.
/// The XFCB adds attribute support to the standard FCB.
/// </summary>
/// <remarks>
/// <para>
/// The Extended FCB structure is 44 bytes (7 bytes header + 37 bytes standard FCB):
/// <list type="bullet">
///   <item>Offset 0x00 (1 byte): Extended FCB flag (must be 0xFF)</item>
///   <item>Offset 0x01 (5 bytes): Reserved</item>
///   <item>Offset 0x06 (1 byte): File attributes</item>
///   <item>Offset 0x07 (37 bytes): Standard FCB</item>
/// </list>
/// </para>
/// <para>
/// Based on FreeDOS kernel implementation: https://github.com/FDOS/kernel/blob/master/hdr/fcb.h
/// </para>
/// </remarks>
public class DosExtendedFileControlBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Total size of an Extended FCB structure in bytes.
    /// </summary>
    public const int StructureSize = 44;

    /// <summary>
    /// The flag value that indicates an Extended FCB (0xFF).
    /// </summary>
    public const byte ExtendedFcbFlag = 0xFF;

    /// <summary>
    /// Size of the extended FCB header in bytes.
    /// </summary>
    public const int HeaderSize = 7;

    // Field offsets
    private const int FlagOffset = 0x00;
    private const int ReservedOffset = 0x01;
    private const int AttributeOffset = 0x06;
    private const int FcbOffset = 0x07;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosExtendedFileControlBlock"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus for reading/writing XFCB data.</param>
    /// <param name="baseAddress">The base address of the XFCB in memory.</param>
    public DosExtendedFileControlBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the extended FCB flag.
    /// Must be 0xFF to indicate an extended FCB.
    /// </summary>
    public byte Flag {
        get => UInt8[FlagOffset];
        set => UInt8[FlagOffset] = value;
    }

    /// <summary>
    /// Gets a value indicating whether this is a valid extended FCB.
    /// </summary>
    public bool IsExtendedFcb => Flag == ExtendedFcbFlag;

    /// <summary>
    /// Gets or sets the file attributes for the extended FCB.
    /// </summary>
    public byte Attribute {
        get => UInt8[AttributeOffset];
        set => UInt8[AttributeOffset] = value;
    }

    /// <summary>
    /// Gets the embedded standard FCB structure.
    /// </summary>
    public DosFileControlBlock Fcb => new(ByteReaderWriter, BaseAddress + FcbOffset);

    /// <summary>
    /// Gets the offset where the standard FCB begins within this extended FCB.
    /// </summary>
    public static int FcbStartOffset => FcbOffset;
}
