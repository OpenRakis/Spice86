namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.Memory.Indexable;

/// <summary>
/// VBE Mode Information Block (256 bytes).
/// This structure contains detailed information about a specific VBE video mode.
/// Returned by Function 01h (ReturnModeInfo).
/// </summary>
public class ModeInfoBlock {
    private readonly IIndexable _memory;
    private readonly uint _address;

    /// <summary>
    /// Size of the ModeInfoBlock structure in bytes.
    /// </summary>
    public const int StructureSize = 256;

    /// <summary>
    /// Initializes a new instance of the ModeInfoBlock class.
    /// </summary>
    /// <param name="memory">The memory interface to read/write the structure.</param>
    /// <param name="address">The physical address where the structure is located.</param>
    public ModeInfoBlock(IIndexable memory, uint address) {
        _memory = memory;
        _address = address;
    }

    /// <summary>
    /// Mode attributes.
    /// Offset: 00h, Size: 2 bytes.
    /// </summary>
    public VbeModeAttribute ModeAttributes {
        get => (VbeModeAttribute)_memory.UInt16[_address];
        set => _memory.UInt16[_address] = (ushort)value;
    }

    /// <summary>
    /// Window A attributes.
    /// Offset: 02h, Size: 1 byte.
    /// </summary>
    public VbeWindowAttribute WindowAAttributes {
        get => (VbeWindowAttribute)_memory.UInt8[_address + 0x02];
        set => _memory.UInt8[_address + 0x02] = (byte)value;
    }

    /// <summary>
    /// Window B attributes.
    /// Offset: 03h, Size: 1 byte.
    /// </summary>
    public VbeWindowAttribute WindowBAttributes {
        get => (VbeWindowAttribute)_memory.UInt8[_address + 0x03];
        set => _memory.UInt8[_address + 0x03] = (byte)value;
    }

    /// <summary>
    /// Window granularity in KB.
    /// Offset: 04h, Size: 2 bytes.
    /// </summary>
    public ushort WindowGranularity {
        get => _memory.UInt16[_address + 0x04];
        set => _memory.UInt16[_address + 0x04] = value;
    }

    /// <summary>
    /// Window size in KB.
    /// Offset: 06h, Size: 2 bytes.
    /// </summary>
    public ushort WindowSize {
        get => _memory.UInt16[_address + 0x06];
        set => _memory.UInt16[_address + 0x06] = value;
    }

    /// <summary>
    /// Window A segment.
    /// Offset: 08h, Size: 2 bytes.
    /// </summary>
    public ushort WindowASegment {
        get => _memory.UInt16[_address + 0x08];
        set => _memory.UInt16[_address + 0x08] = value;
    }

    /// <summary>
    /// Window B segment.
    /// Offset: 0Ah, Size: 2 bytes.
    /// </summary>
    public ushort WindowBSegment {
        get => _memory.UInt16[_address + 0x0A];
        set => _memory.UInt16[_address + 0x0A] = value;
    }

    /// <summary>
    /// Window function pointer (real mode far pointer).
    /// Offset: 0Ch, Size: 4 bytes.
    /// </summary>
    public uint WindowFunctionPtr {
        get => _memory.UInt32[_address + 0x0C];
        set => _memory.UInt32[_address + 0x0C] = value;
    }

    /// <summary>
    /// Bytes per scan line.
    /// Offset: 10h, Size: 2 bytes.
    /// </summary>
    public ushort BytesPerScanLine {
        get => _memory.UInt16[_address + 0x10];
        set => _memory.UInt16[_address + 0x10] = value;
    }

    /// <summary>
    /// Horizontal resolution in pixels.
    /// Offset: 12h, Size: 2 bytes.
    /// </summary>
    public ushort Width {
        get => _memory.UInt16[_address + 0x12];
        set => _memory.UInt16[_address + 0x12] = value;
    }

    /// <summary>
    /// Vertical resolution in pixels.
    /// Offset: 14h, Size: 2 bytes.
    /// </summary>
    public ushort Height {
        get => _memory.UInt16[_address + 0x14];
        set => _memory.UInt16[_address + 0x14] = value;
    }

    /// <summary>
    /// Character cell width in pixels.
    /// Offset: 16h, Size: 1 byte.
    /// </summary>
    public byte CharacterWidth {
        get => _memory.UInt8[_address + 0x16];
        set => _memory.UInt8[_address + 0x16] = value;
    }

    /// <summary>
    /// Character cell height in pixels.
    /// Offset: 17h, Size: 1 byte.
    /// </summary>
    public byte CharacterHeight {
        get => _memory.UInt8[_address + 0x17];
        set => _memory.UInt8[_address + 0x17] = value;
    }

    /// <summary>
    /// Number of memory planes.
    /// Offset: 18h, Size: 1 byte.
    /// </summary>
    public byte NumberOfPlanes {
        get => _memory.UInt8[_address + 0x18];
        set => _memory.UInt8[_address + 0x18] = value;
    }

    /// <summary>
    /// Bits per pixel.
    /// Offset: 19h, Size: 1 byte.
    /// </summary>
    public byte BitsPerPixel {
        get => _memory.UInt8[_address + 0x19];
        set => _memory.UInt8[_address + 0x19] = value;
    }

    /// <summary>
    /// Number of banks.
    /// Offset: 1Ah, Size: 1 byte.
    /// </summary>
    public byte NumberOfBanks {
        get => _memory.UInt8[_address + 0x1A];
        set => _memory.UInt8[_address + 0x1A] = value;
    }

    /// <summary>
    /// Memory model type.
    /// Offset: 1Bh, Size: 1 byte.
    /// </summary>
    public VbeMemoryModel MemoryModel {
        get => (VbeMemoryModel)_memory.UInt8[_address + 0x1B];
        set => _memory.UInt8[_address + 0x1B] = (byte)value;
    }

    /// <summary>
    /// Bank size in KB.
    /// Offset: 1Ch, Size: 1 byte.
    /// </summary>
    public byte BankSize {
        get => _memory.UInt8[_address + 0x1C];
        set => _memory.UInt8[_address + 0x1C] = value;
    }

    /// <summary>
    /// Number of image pages.
    /// Offset: 1Dh, Size: 1 byte.
    /// </summary>
    public byte NumberOfImagePages {
        get => _memory.UInt8[_address + 0x1D];
        set => _memory.UInt8[_address + 0x1D] = value;
    }

    /// <summary>
    /// Reserved (always 1).
    /// Offset: 1Eh, Size: 1 byte.
    /// </summary>
    public byte Reserved1 {
        get => _memory.UInt8[_address + 0x1E];
        set => _memory.UInt8[_address + 0x1E] = value;
    }

    /// <summary>
    /// Red mask size.
    /// Offset: 1Fh, Size: 1 byte.
    /// </summary>
    public byte RedMaskSize {
        get => _memory.UInt8[_address + 0x1F];
        set => _memory.UInt8[_address + 0x1F] = value;
    }

    /// <summary>
    /// Red field position.
    /// Offset: 20h, Size: 1 byte.
    /// </summary>
    public byte RedFieldPosition {
        get => _memory.UInt8[_address + 0x20];
        set => _memory.UInt8[_address + 0x20] = value;
    }

    /// <summary>
    /// Green mask size.
    /// Offset: 21h, Size: 1 byte.
    /// </summary>
    public byte GreenMaskSize {
        get => _memory.UInt8[_address + 0x21];
        set => _memory.UInt8[_address + 0x21] = value;
    }

    /// <summary>
    /// Green field position.
    /// Offset: 22h, Size: 1 byte.
    /// </summary>
    public byte GreenFieldPosition {
        get => _memory.UInt8[_address + 0x22];
        set => _memory.UInt8[_address + 0x22] = value;
    }

    /// <summary>
    /// Blue mask size.
    /// Offset: 23h, Size: 1 byte.
    /// </summary>
    public byte BlueMaskSize {
        get => _memory.UInt8[_address + 0x23];
        set => _memory.UInt8[_address + 0x23] = value;
    }

    /// <summary>
    /// Blue field position.
    /// Offset: 24h, Size: 1 byte.
    /// </summary>
    public byte BlueFieldPosition {
        get => _memory.UInt8[_address + 0x24];
        set => _memory.UInt8[_address + 0x24] = value;
    }

    /// <summary>
    /// Reserved mask size.
    /// Offset: 25h, Size: 1 byte.
    /// </summary>
    public byte ReservedMaskSize {
        get => _memory.UInt8[_address + 0x25];
        set => _memory.UInt8[_address + 0x25] = value;
    }

    /// <summary>
    /// Reserved field position.
    /// Offset: 26h, Size: 1 byte.
    /// </summary>
    public byte ReservedFieldPosition {
        get => _memory.UInt8[_address + 0x26];
        set => _memory.UInt8[_address + 0x26] = value;
    }

    /// <summary>
    /// Direct color mode info.
    /// Offset: 27h, Size: 1 byte.
    /// </summary>
    public VbeDirectColorModeInfo DirectColorModeInfo {
        get => (VbeDirectColorModeInfo)_memory.UInt8[_address + 0x27];
        set => _memory.UInt8[_address + 0x27] = (byte)value;
    }
}
