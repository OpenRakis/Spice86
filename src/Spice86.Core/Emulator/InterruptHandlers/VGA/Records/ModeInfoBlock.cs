namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// VBE Mode Information Block (256 bytes).
/// This structure contains detailed information about a specific VBE video mode.
/// Returned by Function 01h (ReturnModeInfo).
/// </summary>
public class ModeInfoBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Size of the ModeInfoBlock structure in bytes.
    /// </summary>
    public const int StructureSize = 256;

    /// <summary>
    /// Initializes a new instance of the ModeInfoBlock class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory interface to read/write the structure.</param>
    /// <param name="baseAddress">The physical address where the structure is located.</param>
    public ModeInfoBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) 
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Mode attributes.
    /// Offset: 00h, Size: 2 bytes.
    /// </summary>
    public VbeModeAttribute ModeAttributes {
        get => (VbeModeAttribute)UInt16[0x00];
        set => UInt16[0x00] = (ushort)value;
    }

    /// <summary>
    /// Window A attributes.
    /// Offset: 02h, Size: 1 byte.
    /// </summary>
    public VbeWindowAttribute WindowAAttributes {
        get => (VbeWindowAttribute)UInt8[0x02];
        set => UInt8[0x02] = (byte)value;
    }

    /// <summary>
    /// Window B attributes.
    /// Offset: 03h, Size: 1 byte.
    /// </summary>
    public VbeWindowAttribute WindowBAttributes {
        get => (VbeWindowAttribute)UInt8[0x03];
        set => UInt8[0x03] = (byte)value;
    }

    /// <summary>
    /// Window granularity in KB.
    /// Offset: 04h, Size: 2 bytes.
    /// </summary>
    public ushort WindowGranularity {
        get => UInt16[0x04];
        set => UInt16[0x04] = value;
    }

    /// <summary>
    /// Window size in KB.
    /// Offset: 06h, Size: 2 bytes.
    /// </summary>
    public ushort WindowSize {
        get => UInt16[0x06];
        set => UInt16[0x06] = value;
    }

    /// <summary>
    /// Window A segment.
    /// Offset: 08h, Size: 2 bytes.
    /// </summary>
    public ushort WindowASegment {
        get => UInt16[0x08];
        set => UInt16[0x08] = value;
    }

    /// <summary>
    /// Window B segment.
    /// Offset: 0Ah, Size: 2 bytes.
    /// </summary>
    public ushort WindowBSegment {
        get => UInt16[0x0A];
        set => UInt16[0x0A] = value;
    }

    /// <summary>
    /// Window function pointer (real mode far pointer).
    /// Offset: 0Ch, Size: 4 bytes.
    /// </summary>
    public uint WindowFunctionPtr {
        get => UInt32[0x0C];
        set => UInt32[0x0C] = value;
    }

    /// <summary>
    /// Bytes per scan line.
    /// Offset: 10h, Size: 2 bytes.
    /// </summary>
    public ushort BytesPerScanLine {
        get => UInt16[0x10];
        set => UInt16[0x10] = value;
    }

    /// <summary>
    /// Horizontal resolution in pixels.
    /// Offset: 12h, Size: 2 bytes.
    /// </summary>
    public ushort Width {
        get => UInt16[0x12];
        set => UInt16[0x12] = value;
    }

    /// <summary>
    /// Vertical resolution in pixels.
    /// Offset: 14h, Size: 2 bytes.
    /// </summary>
    public ushort Height {
        get => UInt16[0x14];
        set => UInt16[0x14] = value;
    }

    /// <summary>
    /// Character cell width in pixels.
    /// Offset: 16h, Size: 1 byte.
    /// </summary>
    public byte CharacterWidth {
        get => UInt8[0x16];
        set => UInt8[0x16] = value;
    }

    /// <summary>
    /// Character cell height in pixels.
    /// Offset: 17h, Size: 1 byte.
    /// </summary>
    public byte CharacterHeight {
        get => UInt8[0x17];
        set => UInt8[0x17] = value;
    }

    /// <summary>
    /// Number of memory planes.
    /// Offset: 18h, Size: 1 byte.
    /// </summary>
    public byte NumberOfPlanes {
        get => UInt8[0x18];
        set => UInt8[0x18] = value;
    }

    /// <summary>
    /// Bits per pixel.
    /// Offset: 19h, Size: 1 byte.
    /// </summary>
    public byte BitsPerPixel {
        get => UInt8[0x19];
        set => UInt8[0x19] = value;
    }

    /// <summary>
    /// Number of banks.
    /// Offset: 1Ah, Size: 1 byte.
    /// </summary>
    public byte NumberOfBanks {
        get => UInt8[0x1A];
        set => UInt8[0x1A] = value;
    }

    /// <summary>
    /// Memory model type.
    /// Offset: 1Bh, Size: 1 byte.
    /// </summary>
    public VbeMemoryModel MemoryModel {
        get => (VbeMemoryModel)UInt8[0x1B];
        set => UInt8[0x1B] = (byte)value;
    }

    /// <summary>
    /// Bank size in KB.
    /// Offset: 1Ch, Size: 1 byte.
    /// </summary>
    public byte BankSize {
        get => UInt8[0x1C];
        set => UInt8[0x1C] = value;
    }

    /// <summary>
    /// Number of image pages.
    /// Offset: 1Dh, Size: 1 byte.
    /// </summary>
    public byte NumberOfImagePages {
        get => UInt8[0x1D];
        set => UInt8[0x1D] = value;
    }

    /// <summary>
    /// Reserved (always 1).
    /// Offset: 1Eh, Size: 1 byte.
    /// </summary>
    public byte Reserved1 {
        get => UInt8[0x1E];
        set => UInt8[0x1E] = value;
    }

    /// <summary>
    /// Red mask size.
    /// Offset: 1Fh, Size: 1 byte.
    /// </summary>
    public byte RedMaskSize {
        get => UInt8[0x1F];
        set => UInt8[0x1F] = value;
    }

    /// <summary>
    /// Red field position.
    /// Offset: 20h, Size: 1 byte.
    /// </summary>
    public byte RedFieldPosition {
        get => UInt8[0x20];
        set => UInt8[0x20] = value;
    }

    /// <summary>
    /// Green mask size.
    /// Offset: 21h, Size: 1 byte.
    /// </summary>
    public byte GreenMaskSize {
        get => UInt8[0x21];
        set => UInt8[0x21] = value;
    }

    /// <summary>
    /// Green field position.
    /// Offset: 22h, Size: 1 byte.
    /// </summary>
    public byte GreenFieldPosition {
        get => UInt8[0x22];
        set => UInt8[0x22] = value;
    }

    /// <summary>
    /// Blue mask size.
    /// Offset: 23h, Size: 1 byte.
    /// </summary>
    public byte BlueMaskSize {
        get => UInt8[0x23];
        set => UInt8[0x23] = value;
    }

    /// <summary>
    /// Blue field position.
    /// Offset: 24h, Size: 1 byte.
    /// </summary>
    public byte BlueFieldPosition {
        get => UInt8[0x24];
        set => UInt8[0x24] = value;
    }

    /// <summary>
    /// Reserved mask size.
    /// Offset: 25h, Size: 1 byte.
    /// </summary>
    public byte ReservedMaskSize {
        get => UInt8[0x25];
        set => UInt8[0x25] = value;
    }

    /// <summary>
    /// Reserved field position.
    /// Offset: 26h, Size: 1 byte.
    /// </summary>
    public byte ReservedFieldPosition {
        get => UInt8[0x26];
        set => UInt8[0x26] = value;
    }

    /// <summary>
    /// Direct color mode info.
    /// Offset: 27h, Size: 1 byte.
    /// </summary>
    public VbeDirectColorModeInfo DirectColorModeInfo {
        get => (VbeDirectColorModeInfo)UInt8[0x27];
        set => UInt8[0x27] = (byte)value;
    }
}
