namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents the VBE Mode Info Block structure (VBE 1.0/1.2).
/// "This function returns information about a specific Super VGA video mode that was
/// returned by Function 0. The function fills a mode information block structure
/// at the address specified by the caller. The mode information block size is
/// maximum 256 bytes."
/// Returned by VBE Function 01h - Return VBE Mode Information.
/// Size is 256 bytes.
/// </summary>
public class VbeModeInfoBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance of the <see cref="VbeModeInfoBlock"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The base address of the structure in memory.</param>
    public VbeModeInfoBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// "The ModeAttributes field describes certain important characteristics of the
    /// video mode. Bit D0 specifies whether this mode can be initialized in the
    /// present video configuration."
    /// Offset: 0x00, Size: 2 bytes (word).
    /// Bit definitions:
    /// "D0 = Mode supported in hardware (0 = Mode not supported, 1 = Mode supported)
    /// D1 = 1 (Reserved)
    /// D2 = Output functions supported by BIOS (0 = not supported, 1 = supported)
    /// D3 = Monochrome/color mode (0 = Monochrome, 1 = Color)
    /// D4 = Mode type (0 = Text mode, 1 = Graphics mode)
    /// D5-D15 = Reserved"
    /// </summary>
    public ushort ModeAttributes {
        get => UInt16[0x00];
        set => UInt16[0x00] = value;
    }

    /// <summary>
    /// "The WinAAttributes and WinBAttributes describe the characteristics of the CPU
    /// windowing scheme such as whether the windows exist and are read/writeable."
    /// Window A attributes. Offset: 0x02, Size: 1 byte.
    /// "D0 = Window supported (0 = not supported, 1 = supported)
    /// D1 = Window readable (0 = not readable, 1 = readable)
    /// D2 = Window writeable (0 = not writeable, 1 = writeable)
    /// D3-D7 = Reserved"
    /// </summary>
    public byte WindowAAttributes {
        get => UInt8[0x02];
        set => UInt8[0x02] = value;
    }

    /// <summary>
    /// "The WinAAttributes and WinBAttributes describe the characteristics of the CPU
    /// windowing scheme such as whether the windows exist and are read/writeable."
    /// Window B attributes (same bits as WindowA). Offset: 0x03, Size: 1 byte.
    /// </summary>
    public byte WindowBAttributes {
        get => UInt8[0x03];
        set => UInt8[0x03] = value;
    }

    /// <summary>
    /// "WinGranularity specifies the smallest boundary, in KB, on which the window can
    /// be placed in the video memory. The value of this field is undefined if Bit D0
    /// of the appropriate WinAttributes field is not set."
    /// Offset: 0x04, Size: 2 bytes (word).
    /// </summary>
    public ushort WindowGranularity {
        get => UInt16[0x04];
        set => UInt16[0x04] = value;
    }

    /// <summary>
    /// "WinSize specifies the size of the window in KB."
    /// Offset: 0x06, Size: 2 bytes (word).
    /// </summary>
    public ushort WindowSize {
        get => UInt16[0x06];
        set => UInt16[0x06] = value;
    }

    /// <summary>
    /// "WinASegment and WinBSegment address specify the segment addresses where the
    /// windows are located in CPU address space."
    /// Window A start segment. Offset: 0x08, Size: 2 bytes (word).
    /// </summary>
    public ushort WindowASegment {
        get => UInt16[0x08];
        set => UInt16[0x08] = value;
    }

    /// <summary>
    /// "WinASegment and WinBSegment address specify the segment addresses where the
    /// windows are located in CPU address space."
    /// Window B start segment. Offset: 0x0A, Size: 2 bytes (word).
    /// </summary>
    public ushort WindowBSegment {
        get => UInt16[0x0A];
        set => UInt16[0x0A] = value;
    }

    /// <summary>
    /// "WinFuncAddr specifies the address of the CPU video memory windowing function.
    /// The windowing function can be invoked either through VESA BIOS function 05h, or
    /// by calling the function directly."
    /// Pointer to window positioning function (far pointer stored as offset:segment).
    /// Offset part at 0x0C, Size: 2 bytes (word).
    /// </summary>
    public ushort WindowFunctionOffset {
        get => UInt16[0x0C];
        set => UInt16[0x0C] = value;
    }

    /// <summary>
    /// "WinFuncAddr specifies the address of the CPU video memory windowing function."
    /// Pointer to window positioning function (far pointer stored as offset:segment).
    /// Segment part at 0x0E, Size: 2 bytes (word).
    /// </summary>
    public ushort WindowFunctionSegment {
        get => UInt16[0x0E];
        set => UInt16[0x0E] = value;
    }

    /// <summary>
    /// "The BytesPerScanline field specifies how many bytes each logical scanline
    /// consists of. The logical scanline could be equal to or larger then the
    /// displayed scanline."
    /// Offset: 0x10, Size: 2 bytes (word).
    /// For planar modes (4-bit), this is bytes per plane.
    /// For packed pixel modes, this is total bytes per line.
    /// </summary>
    public ushort BytesPerScanLine {
        get => UInt16[0x10];
        set => UInt16[0x10] = value;
    }

    /// <summary>
    /// "The XResolution and YResolution specify the width and height of the video mode.
    /// In graphics modes, this resolution is in units of pixels."
    /// Horizontal resolution in pixels. Offset: 0x12, Size: 2 bytes (word).
    /// </summary>
    public ushort XResolution {
        get => UInt16[0x12];
        set => UInt16[0x12] = value;
    }

    /// <summary>
    /// "The XResolution and YResolution specify the width and height of the video mode.
    /// In graphics modes, this resolution is in units of pixels."
    /// Vertical resolution in pixels. Offset: 0x14, Size: 2 bytes (word).
    /// </summary>
    public ushort YResolution {
        get => UInt16[0x14];
        set => UInt16[0x14] = value;
    }

    /// <summary>
    /// "The XCharCellSize and YCharSellSize specify the size of the character cell in
    /// pixels."
    /// Character cell width in pixels. Offset: 0x16, Size: 1 byte.
    /// </summary>
    public byte XCharSize {
        get => UInt8[0x16];
        set => UInt8[0x16] = value;
    }

    /// <summary>
    /// "The XCharCellSize and YCharSellSize specify the size of the character cell in
    /// pixels."
    /// Character cell height in pixels. Offset: 0x17, Size: 1 byte.
    /// </summary>
    public byte YCharSize {
        get => UInt8[0x17];
        set => UInt8[0x17] = value;
    }

    /// <summary>
    /// "The NumberOfPlanes field specifies the number of memory planes available to
    /// software in that mode. For standard 16-color VGA graphics, this would be set to
    /// 4. For standard packed pixel modes, the field would be set to 1."
    /// Offset: 0x18, Size: 1 byte.
    /// </summary>
    public byte NumberOfPlanes {
        get => UInt8[0x18];
        set => UInt8[0x18] = value;
    }

    /// <summary>
    /// "The BitsPerPixel field specifies the total number of bits that define the color
    /// of one pixel. For example, a standard VGA 4 Plane 16-color graphics mode would
    /// have a 4 in this field and a packed pixel 256-color graphics mode would specify
    /// 8 in this field."
    /// Offset: 0x19, Size: 1 byte.
    /// </summary>
    public byte BitsPerPixel {
        get => UInt8[0x19];
        set => UInt8[0x19] = value;
    }

    /// <summary>
    /// "The NumberOfBanks field specifies the number of banks in which the scan lines
    /// are grouped. The remainder from dividing the scan line number by the number of
    /// banks is the bank that contains the scan line and the quotient is the scan line
    /// number within the bank."
    /// Offset: 0x1A, Size: 1 byte.
    /// </summary>
    public byte NumberOfBanks {
        get => UInt8[0x1A];
        set => UInt8[0x1A] = value;
    }

    /// <summary>
    /// "The MemoryModel field specifies the general type of memory organization used in
    /// this mode."
    /// Offset: 0x1B, Size: 1 byte.
    /// "00h = Text mode
    /// 01h = CGA graphics
    /// 02h = Hercules graphics
    /// 03h = 4-plane planar
    /// 04h = Packed pixel
    /// 05h = Non-chain 4, 256 color
    /// 06h = Direct Color
    /// 07h = YUV.
    /// 08h-0Fh = Reserved, to be defined by VESA
    /// 10h-FFh = To be defined by OEM"
    /// </summary>
    public byte MemoryModel {
        get => UInt8[0x1B];
        set => UInt8[0x1B] = value;
    }

    /// <summary>
    /// "The BankSize field specifies the size of a bank (group of scan lines) in units
    /// of 1KB. For CGA and Hercules graphics modes this is 8, as each bank is 8192
    /// bytes in length."
    /// Offset: 0x1C, Size: 1 byte.
    /// </summary>
    public byte BankSize {
        get => UInt8[0x1C];
        set => UInt8[0x1C] = value;
    }

    /// <summary>
    /// "The NumberOfImagePages field specifies the number of additional complete display
    /// images that will fit into the VGA's memory, at one time, in this mode."
    /// Offset: 0x1D, Size: 1 byte.
    /// </summary>
    public byte NumberOfImagePages {
        get => UInt8[0x1D];
        set => UInt8[0x1D] = value;
    }

    /// <summary>
    /// "The Reserved field has been defined to support a future VESA BIOS extension
    /// feature and will always be set to one in this version."
    /// Offset: 0x1E, Size: 1 byte.
    /// </summary>
    public byte Reserved1 {
        get => UInt8[0x1E];
        set => UInt8[0x1E] = value;
    }

    /// <summary>
    /// "The RedMaskSize, GreenMaskSize, BlueMaskSize, and RsvdMaskSize fields define the
    /// size, in bits, of the red, green, and blue components of a direct color pixel."
    /// Red mask size (number of bits). Offset: 0x1F, Size: 1 byte.
    /// </summary>
    public byte RedMaskSize {
        get => UInt8[0x1F];
        set => UInt8[0x1F] = value;
    }

    /// <summary>
    /// "The RedFieldPosition, GreenFieldPosition, BlueFieldPosition, and
    /// RsvdFieldPosition fields define the bit position within the direct color pixel
    /// or YUV pixel of the least significant bit of the respective color component."
    /// Red field position (bit offset). Offset: 0x20, Size: 1 byte.
    /// </summary>
    public byte RedFieldPosition {
        get => UInt8[0x20];
        set => UInt8[0x20] = value;
    }

    /// <summary>
    /// "The RedMaskSize, GreenMaskSize, BlueMaskSize, and RsvdMaskSize fields define the
    /// size, in bits, of the red, green, and blue components of a direct color pixel."
    /// Green mask size (number of bits). Offset: 0x21, Size: 1 byte.
    /// </summary>
    public byte GreenMaskSize {
        get => UInt8[0x21];
        set => UInt8[0x21] = value;
    }

    /// <summary>
    /// "The RedFieldPosition, GreenFieldPosition, BlueFieldPosition, and
    /// RsvdFieldPosition fields define the bit position within the direct color pixel
    /// or YUV pixel of the least significant bit of the respective color component."
    /// Green field position (bit offset). Offset: 0x22, Size: 1 byte.
    /// </summary>
    public byte GreenFieldPosition {
        get => UInt8[0x22];
        set => UInt8[0x22] = value;
    }

    /// <summary>
    /// "The RedMaskSize, GreenMaskSize, BlueMaskSize, and RsvdMaskSize fields define the
    /// size, in bits, of the red, green, and blue components of a direct color pixel."
    /// Blue mask size (number of bits). Offset: 0x23, Size: 1 byte.
    /// </summary>
    public byte BlueMaskSize {
        get => UInt8[0x23];
        set => UInt8[0x23] = value;
    }

    /// <summary>
    /// "The RedFieldPosition, GreenFieldPosition, BlueFieldPosition, and
    /// RsvdFieldPosition fields define the bit position within the direct color pixel
    /// or YUV pixel of the least significant bit of the respective color component."
    /// Blue field position (bit offset). Offset: 0x24, Size: 1 byte.
    /// </summary>
    public byte BlueFieldPosition {
        get => UInt8[0x24];
        set => UInt8[0x24] = value;
    }

    /// <summary>
    /// "The RedMaskSize, GreenMaskSize, BlueMaskSize, and RsvdMaskSize fields define the
    /// size, in bits, of the red, green, and blue components of a direct color pixel."
    /// Reserved mask size (number of bits). Offset: 0x25, Size: 1 byte.
    /// </summary>
    public byte ReservedMaskSize {
        get => UInt8[0x25];
        set => UInt8[0x25] = value;
    }

    /// <summary>
    /// "The RedFieldPosition, GreenFieldPosition, BlueFieldPosition, and
    /// RsvdFieldPosition fields define the bit position within the direct color pixel
    /// or YUV pixel of the least significant bit of the respective color component."
    /// Reserved field position (bit offset). Offset: 0x26, Size: 1 byte.
    /// </summary>
    public byte ReservedFieldPosition {
        get => UInt8[0x26];
        set => UInt8[0x26] = value;
    }

    /// <summary>
    /// "The DirectColorModeInfo field describes important characteristics of direct
    /// color modes. Bit D0 specifies whether the color ramp of the DAC is fixed or
    /// programmable."
    /// Offset: 0x27, Size: 1 byte.
    /// "D0 = Color ramp is fixed/programmable (0 = fixed, 1 = programmable)
    /// D1 = Bits in Rsvd field are usable/reserved (0 = reserved, 1 = usable)"
    /// </summary>
    public byte DirectColorModeInfo {
        get => UInt8[0x27];
        set => UInt8[0x27] = value;
    }

    /// <summary>
    /// "Version 1.1 and later VESA BIOS extensions will zero out all unused fields in
    /// the Mode Information Block, always returning exactly 256 bytes."
    /// Clears the entire 256-byte mode info block.
    /// </summary>
    public void Clear() {
        for (uint i = 0; i < 256; i++) {
            UInt8[i] = 0;
        }
    }
}