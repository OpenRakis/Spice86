namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
///     A VGA BIOS implementation.
/// </summary>
public class VgaBios : InterruptHandler, IVideoInt10Handler, IVesaBiosExtension {
    private readonly BiosDataArea _biosDataArea;
    private readonly ILoggerService _logger;
    private readonly IVgaFunctionality _vgaFunctions;

    /// <summary>
    /// "Several new BIOS calls have been defined to support Super VGA modes. For
    /// maximum compatibility with the standard VGA BIOS, these calls are grouped under
    /// one function number."
    /// VBE (VESA BIOS Extension) function codes (AL register values when AH=4Fh).
    /// "The designated Super VGA extended function number is 4Fh."
    /// </summary>
    private enum VbeFunction : byte {
        /// <summary>
        /// "Function 00h - Return Super VGA Information"
        /// </summary>
        GetControllerInfo = 0x00,
        /// <summary>
        /// "Function 01h - Return Super VGA mode information"
        /// </summary>
        GetModeInfo = 0x01,
        /// <summary>
        /// "Function 02h - Set Super VGA video mode"
        /// </summary>
        SetMode = 0x02
    }

    /// <summary>
    /// "Every function returns status information in the AX register. The format of the
    /// status word is as follows:
    /// AL == 4Fh: Function is supported
    /// AL != 4Fh: Function is not supported
    /// AH == 00h: Function call successful
    /// AH == 01h: Function call failed"
    /// VBE status codes returned in AX register.
    /// </summary>
    private enum VbeStatus : ushort {
        /// <summary>
        /// "AL == 4Fh: Function is supported, AH == 00h: Function call successful"
        /// </summary>
        Success = 0x004F,
        /// <summary>
        /// "AL == 4Fh: Function is supported, AH == 01h: Function call failed"
        /// </summary>
        Failed = 0x014F
    }

    /// <summary>
    /// INT 10h AH=12h (Video Subsystem Configuration) subfunctions (BL register values).
    /// </summary>
    private enum VideoSubsystemFunction : byte {
        EgaVgaInformation = 0x10,
        SelectScanLines = 0x30,
        DefaultPaletteLoading = 0x31,
        VideoEnableDisable = 0x32,
        SummingToGrayScales = 0x33,
        CursorEmulation = 0x34,
        DisplaySwitch = 0x35,
        VideoScreenOnOff = 0x36
    }

    /// <summary>
    /// "The format of VESA mode numbers is as follows:
    /// D0-D8 = Mode number (If D8 == 0, not VESA defined; If D8 == 1, VESA defined)
    /// D9-D14 = Reserved by VESA for future expansion (= 0)
    /// D15 = Reserved (= 0)"
    /// 
    /// VBE mode flags (used in BX register for VBE Set Mode function).
    /// "Input: BX = Video mode
    /// D0-D14 = Video mode
    /// D15 = Clear memory flag (0 = Clear video memory, 1 = Don't clear video memory)"
    /// 
    /// Note: Bit 14 (UseLinearFrameBuffer) is VBE 2.0+ only, ignored in VBE 1.0/1.2.
    /// </summary>
    [Flags]
    private enum VbeModeFlags : ushort {
        None = 0x0000,
        /// <summary>
        /// Use linear frame buffer (VBE 2.0+ only, ignored in VBE 1.0/1.2).
        /// </summary>
        UseLinearFrameBuffer = 0x4000,
        /// <summary>
        /// "D15 = Clear memory flag (1 = Don't clear video memory)"
        /// </summary>
        DontClearMemory = 0x8000,
        /// <summary>
        /// "D0-D8 = Mode number" (bits 0-8, mask for extracting mode number)
        /// </summary>
        ModeNumberMask = 0x01FF
    }

    /// <summary>
    /// VBE-related constants from VESA Super VGA BIOS Extension Standard #VS911022, VBE Version 1.2.
    /// </summary>
    private static class VbeConstants {
        /// <summary>
        /// "The current VESA version number is 1.2."
        /// VBE 1.0 version number in BCD format (major.minor = 0x0100 = 1.0).
        /// </summary>
        public const ushort Version10 = 0x0100;

        /// <summary>
        /// "D0 = DAC is switchable (0 = DAC is fixed width, with 6-bits per primary color,
        /// 1 = DAC width is switchable)"
        /// Capability bit indicating DAC width is switchable.
        /// </summary>
        public const uint DacSwitchableCapability = 0x00000001;

        /// <summary>
        /// "The TotalMemory field indicates the amount of memory installed on the VGA
        /// board. Its value represents the number of 64kb blocks of memory currently
        /// installed."
        /// Total memory: 1MB = 16 blocks of 64KB each.
        /// </summary>
        public const ushort TotalMemory1MB = 16;

        /// <summary>
        /// OEM identification string for Spice86 VBE implementation.
        /// </summary>
        public const string OemString = "Spice86 VBE";

        /// <summary>
        /// Offset from VbeInfoBlock base where OEM string is written (beyond 256-byte structure).
        /// </summary>
        public const uint OemStringOffset = 256;

        /// <summary>
        /// Offset from VbeInfoBlock base where mode list is written (after OEM string).
        /// </summary>
        public const uint ModeListOffset = 280;

        /// <summary>
        /// "The list of mode numbers is terminated by a -1 (0FFFFh)."
        /// Mode list terminator value.
        /// </summary>
        public const ushort ModeListTerminator = 0xFFFF;

        /// <summary>
        /// "To date, VESA has defined a 7-bit video mode number, 6Ah, for the 800x600,
        /// 16-color, 4-plane graphics mode. The corresponding 15-bit mode number for this
        /// mode is 102h."
        /// VESA mode 102h: 800x600, 16 colors (4-plane planar).
        /// </summary>
        public const ushort VesaMode800x600x16 = 0x102;

        /// <summary>
        /// Internal VGA mode 6Ah corresponding to VESA mode 102h (800x600x16).
        /// </summary>
        public const int InternalMode800x600x16 = 0x6A;
    }

    /// <summary>
    /// VBE Mode Info Block constants.
    /// </summary>
    private static class VbeModeInfoConstants {
        /// <summary>
        /// Mode attributes: D0=1 (supported), D1=1 (extended info), D2=0, D3=1 (color), D4=1 (graphics) = 0x001B
        /// </summary>
        public const ushort ModeAttributesSupported = 0x001B;
        /// <summary>
        /// "D0 = Window supported, D1 = Window readable, D2 = Window writeable" = 0x07
        /// </summary>
        public const byte WindowAttributesReadWriteSupported = 0x07;
        public const byte WindowAttributesNotSupported = 0x00;
        /// <summary>
        /// "WinGranularity specifies the smallest boundary, in KB" = 64KB
        /// </summary>
        public const ushort WindowGranularity64KB = 64;
        /// <summary>
        /// "WinSize specifies the size of the window in KB" = 64KB
        /// </summary>
        public const ushort WindowSize64KB = 64;
        /// <summary>
        /// "WinASegment address...in CPU address space" = 0xA000
        /// </summary>
        public const ushort WindowASegmentAddress = 0xA000;
        public const ushort WindowBSegmentAddress = 0x0000;
        /// <summary>
        /// "The XCharCellSize...size of the character cell in pixels" = 8
        /// </summary>
        public const byte CharWidth = 8;
        /// <summary>
        /// "The YCharSellSize...size of the character cell in pixels" = 16
        /// </summary>
        public const byte CharHeight = 16;
        /// <summary>
        /// "For modes that don't have scanline banks...this field should be set to 1"
        /// </summary>
        public const byte SingleBank = 1;
        public const byte BankSize64KB = 64;
        /// <summary>
        /// "The NumberOfImagePages field specifies the number of additional...images"
        /// </summary>
        public const byte NoImagePages = 0;
        /// <summary>
        /// "The Reserved field...will always be set to one in this version"
        /// </summary>
        public const byte Reserved = 1;
        /// <summary>
        /// "03h = 4-plane planar"
        /// </summary>
        public const byte MemoryModelPlanar = 3;
        /// <summary>
        /// "04h = Packed pixel"
        /// </summary>
        public const byte MemoryModelPackedPixel = 4;
        /// <summary>
        /// "06h = Direct Color"
        /// </summary>
        public const byte MemoryModelDirectColor = 6;
        /// <summary>
        /// "the MaskSize values for a Direct Color 5:6:5 mode would be 5, 6, 5"
        /// </summary>
        public const byte RedGreenBlueMaskSize = 5;
        public const byte RedGreenBlueMaskSize8 = 8;
        /// <summary>
        /// "the MaskSize values for a Direct Color 5:6:5 mode would be 5, 6, 5" - green=6
        /// </summary>
        public const byte GreenMaskSize6Bit = 6;
    }

    /// <summary>
    /// Common VGA/BIOS constants.
    /// </summary>
    private static class BiosConstants {
        public const byte MaxVideoPage = 7;
        public const byte MaxPaletteRegister = 0x0F;
        public const byte FunctionSupported = 0x1A;
        public const byte SubfunctionSuccess = 0x12;
        public const byte NoSecondaryDisplay = 0x00;
        public const byte DefaultDisplayCombinationCode = 0x08;
        public const byte Memory256KB = 0x03;
        public const ushort VideoControl80x25Color = 0x20;
        public const byte DefaultModeSetControl = 0x51;
        public const byte ModeAbove7Return = 0x20;
        public const byte Mode6Return = 0x3F;
        public const byte ModeBelow7Return = 0x30;
        public const byte VideoModeMask = 0x7F;
        public const byte DontClearMemoryFlag = 0x80;
        public const byte IncludeAttributesFlag = 0x02;
        public const byte UpdateCursorPositionFlag = 0x01;
        public const byte ColorModeMemory = 0x01;
        public const byte VideoControlBitMask = 0x80;
        public const ushort EquipmentListFlagsMask = 0x30;
        public const int ScanLines200 = 200;
        public const int ScanLines350 = 350;
        public const int ScanLines400 = 400;
        public const byte CursorTypeMask = 0x3F;
        public const byte CursorEndMask = 0x1F;
        public const uint StaticFunctionalityAllModes = 0x000FFFFF;
        public const byte StaticFunctionalityAllScanLines = 0x07;
    }

    /// <summary>
    ///     VGA BIOS constructor.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="vgaFunctions">Provides vga functionality to use by the interrupt handler</param>
    /// <param name="biosDataArea">Contains the global bios data values</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public VgaBios(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, IVgaFunctionality vgaFunctions, BiosDataArea biosDataArea, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _vgaFunctions = vgaFunctions;
        _logger = loggerService;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Initializing VGA BIOS");
        }
        FillDispatchTable();

        InitializeBiosArea();
        InitializeStaticFunctionalityTable();
    }

    /// <summary>
    /// Represents the VBE Info Block structure (VBE 1.0/1.2).
    /// "The purpose of this function is to provide information to the calling program
    /// about the general capabilities of the Super VGA environment. The function fills
    /// an information block structure at the address specified by the caller.
    /// The information block size is 256 bytes."
    /// Returned by VBE Function 00h - Return VBE Controller Information.
    /// Minimum size is 256 bytes, but callers should provide ~512 bytes for OEM string and mode list.
    /// </summary>
    public class VbeInfoBlock : MemoryBasedDataStructure {
        private const int SignatureLength = 4;
        private const int OemStringMaxLength = 256;

        /// <summary>
        /// Initializes a new instance of the <see cref="VbeInfoBlock"/> class.
        /// </summary>
        /// <param name="byteReaderWriter">The memory bus.</param>
        /// <param name="baseAddress">The base address of the structure in memory.</param>
        public VbeInfoBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
            : base(byteReaderWriter, baseAddress) {
        }

        /// <summary>
        /// "The VESASignature field contains the characters 'VESA' if this is a valid block."
        /// Gets or sets the VBE signature (4 bytes: "VESA" for VBE 1.x, "VBE2" for VBE 2.0+).
        /// Offset: 0x00, Size: 4 bytes.
        /// </summary>
        public string Signature {
            get => GetZeroTerminatedString(0x00, SignatureLength);
            set {
                string truncated = value.Length >= SignatureLength
                    ? value[..SignatureLength]
                    : value;
                for (int i = 0; i < SignatureLength; i++) {
                    UInt8[0x00 + i] = i < truncated.Length ? (byte)truncated[i] : (byte)0;
                }
            }
        }

        /// <summary>
        /// "The VESAVersion is a binary field which specifies what level of the VESA
        /// standard the Super VGA BIOS conforms to. The higher byte specifies the major
        /// version number. The lower byte specifies the minor version number. The current
        /// VESA version number is 1.2."
        /// Gets or sets the VBE version (BCD format: 0x0100 = 1.0, 0x0102 = 1.2, 0x0200 = 2.0).
        /// Offset: 0x04, Size: 2 bytes (word).
        /// </summary>
        public ushort Version {
            get => UInt16[0x04];
            set => UInt16[0x04] = value;
        }

        /// <summary>
        /// "The OEMStringPtr is a far pointer to a null terminated OEM-defined string."
        /// Gets or sets the pointer to OEM string (far pointer stored as offset:segment).
        /// Offset part at 0x06, Size: 2 bytes (word).
        /// </summary>
        public ushort OemStringOffset {
            get => UInt16[0x06];
            set => UInt16[0x06] = value;
        }

        /// <summary>
        /// "The OEMStringPtr is a far pointer to a null terminated OEM-defined string."
        /// Gets or sets the pointer to OEM string (far pointer stored as offset:segment).
        /// Segment part at 0x08, Size: 2 bytes (word).
        /// </summary>
        public ushort OemStringSegment {
            get => UInt16[0x08];
            set => UInt16[0x08] = value;
        }

        /// <summary>
        /// "The Capabilities field describes what general features are supported in the
        /// video environment. The bits are defined as follows:
        /// D0 = DAC is switchable (0 = DAC is fixed width, with 6-bits per primary color,
        /// 1 = DAC width is switchable)
        /// D1-31 = Reserved"
        /// Gets or sets the capabilities flags (4 bytes).
        /// Offset: 0x0A, Size: 4 bytes (dword).
        /// </summary>
        public uint Capabilities {
            get => UInt32[0x0A];
            set => UInt32[0x0A] = value;
        }

        /// <summary>
        /// "The VideoModePtr points to a list of supported Super VGA (VESA-defined as well
        /// as OEM-specific) mode numbers. Each mode number occupies one word (16 bits).
        /// The list of mode numbers is terminated by a -1 (0FFFFh)."
        /// Gets or sets the pointer to video mode list (far pointer stored as offset:segment).
        /// Offset part at 0x0E, Size: 2 bytes (word).
        /// Points to array of ushort mode numbers, terminated by 0xFFFF.
        /// </summary>
        public ushort VideoModeListOffset {
            get => UInt16[0x0E];
            set => UInt16[0x0E] = value;
        }

        /// <summary>
        /// "The VideoModePtr points to a list of supported Super VGA (VESA-defined as well
        /// as OEM-specific) mode numbers."
        /// Gets or sets the pointer to video mode list (far pointer stored as offset:segment).
        /// Segment part at 0x10, Size: 2 bytes (word).
        /// </summary>
        public ushort VideoModeListSegment {
            get => UInt16[0x10];
            set => UInt16[0x10] = value;
        }

        /// <summary>
        /// "The TotalMemory field indicates the amount of memory installed on the VGA
        /// board. Its value represents the number of 64kb blocks of memory currently
        /// installed."
        /// Gets or sets the total memory in 64KB blocks.
        /// Offset: 0x12, Size: 2 bytes (word).
        /// </summary>
        public ushort TotalMemory {
            get => UInt16[0x12];
            set => UInt16[0x12] = value;
        }

        /// <summary>
        /// Writes the OEM string at the specified address (typically beyond the main structure).
        /// </summary>
        /// <param name="oemString">The OEM string to write.</param>
        /// <param name="offsetFromBase">Offset from base address where to write the string.</param>
        public void WriteOemString(string oemString, uint offsetFromBase) {
            string truncated = oemString.Length >= OemStringMaxLength
                ? oemString[..(OemStringMaxLength - 1)]
                : oemString;
            SetZeroTerminatedString(offsetFromBase, truncated, OemStringMaxLength);
        }

        /// <summary>
        /// "The list of mode numbers is terminated by a -1 (0FFFFh)."
        /// Writes the video mode list at the specified address.
        /// </summary>
        /// <param name="modes">Array of mode numbers (will be terminated with 0xFFFF).</param>
        /// <param name="offsetFromBase">Offset from base address where to write the mode list.</param>
        public void WriteModeList(ushort[] modes, uint offsetFromBase) {
            for (int i = 0; i < modes.Length; i++) {
                UInt16[offsetFromBase + (uint)(i * 2)] = modes[i];
            }
            UInt16[offsetFromBase + (uint)(modes.Length * 2)] = 0xFFFF;
        }
    }

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


    /// <summary>
    ///     The interrupt vector this class handles.
    /// </summary>
    public override byte VectorNumber => 0x10;

    /// <inheritdoc />
    public void WriteString() {
        CursorPosition cursorPosition = new(State.DL, State.DH, State.BH);
        ushort length = State.CX;
        ushort segment = State.ES;
        ushort offset = State.BP;
        byte attribute = State.BL;
        bool includeAttributes = (State.AL & BiosConstants.IncludeAttributesFlag) != 0;
        bool updateCursorPosition = (State.AL & BiosConstants.UpdateCursorPositionFlag) != 0;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
            string str = Memory.GetZeroTerminatedString(address, State.CX);
            _logger.Debug("{ClassName} INT 10 13 {MethodName}: {String} at {X},{Y} attribute: 0x{Attribute:X2}",
                nameof(VgaBios), nameof(WriteString), str, cursorPosition.X, cursorPosition.Y, includeAttributes ? "included" : attribute);
        }
        _vgaFunctions.WriteString(segment, offset, length, includeAttributes, attribute, cursorPosition, updateCursorPosition);
    }

    /// <inheritdoc />
    public void GetSetDisplayCombinationCode() {
        switch (State.AL) {
            case 0x00: {
                    State.AL = BiosConstants.FunctionSupported;
                    State.BL = _biosDataArea.DisplayCombinationCode;
                    State.BH = BiosConstants.NoSecondaryDisplay;
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        _logger.Debug("{ClassName} INT 10 1A {MethodName} - Get: DCC 0x{Dcc:X2}",
                            nameof(VgaBios), nameof(GetSetDisplayCombinationCode), State.BL);
                    }
                    break;
                }
            case 0x01: {
                    State.AL = BiosConstants.FunctionSupported;
                    _biosDataArea.DisplayCombinationCode = State.BL;
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        _logger.Debug("{ClassName} INT 10 1A {MethodName} - Set: DCC 0x{Dcc:X2}",
                            nameof(VgaBios), nameof(GetSetDisplayCombinationCode), State.BL);
                    }
                    break;
                }
            default: {
                    throw new NotSupportedException($"AL=0x{State.AL:X2} is not a valid subFunction for INT 10 1A");
                }
        }
    }

    /// <inheritdoc />
    public void VideoSubsystemConfiguration() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT 10 12 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(LoadFontInfo), State.BL);
        }
        switch ((VideoSubsystemFunction)State.BL) {
            case VideoSubsystemFunction.EgaVgaInformation:
                EgaVgaInformation();
                break;
            case VideoSubsystemFunction.SelectScanLines:
                SelectScanLines();
                break;
            case VideoSubsystemFunction.DefaultPaletteLoading:
                DefaultPaletteLoading();
                break;
            case VideoSubsystemFunction.VideoEnableDisable:
                VideoEnableDisable();
                break;
            case VideoSubsystemFunction.SummingToGrayScales:
                SummingToGrayScales();
                break;
            case VideoSubsystemFunction.CursorEmulation:
                CursorEmulation();
                break;
            case VideoSubsystemFunction.DisplaySwitch:
                DisplaySwitch();
                break;
            case VideoSubsystemFunction.VideoScreenOnOff:
                VideoScreenOnOff();
                break;
            default:
                // Do not fail in case the index is not valid, this is the behaviour of the VGA bios and some programs expect this (prince of persia, checkit).
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning("BL={BL} is not a valid subFunction for INT 10 12", ConvertUtils.ToHex8(State.BL));
                }
                break;
        }
    }

    /// <inheritdoc />
    public void LoadFontInfo() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT 10 11 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(LoadFontInfo), State.AL);
        }
        switch (State.AL) {
            case 0x00:
                _vgaFunctions.LoadUserFont(State.ES, State.BP, State.CX, State.DX, State.BL, State.BH);
                break;
            case 0x01:
                _vgaFunctions.LoadFont(Fonts.VgaFont14, 0x100, 0, State.BL, 14);
                break;
            case 0x02:
                _vgaFunctions.LoadFont(Fonts.VgaFont8, 0x100, 0, State.BL, 8);
                break;
            case 0x03:
                SetBlockSpecifier(State.BL);
                break;
            case 0x04:
                _vgaFunctions.LoadFont(Fonts.VgaFont16, 0x100, 0, State.BL, 16);
                break;
            case 0x10:
                _vgaFunctions.LoadUserFont(State.ES, State.BP, State.CX, State.DX, State.BL, State.BH);
                _vgaFunctions.SetScanLines(State.BH);
                break;
            case 0x11:
                _vgaFunctions.LoadFont(Fonts.VgaFont14, 0x100, 0, State.BL, 14);
                _vgaFunctions.SetScanLines(14);
                break;
            case 0x12:
                _vgaFunctions.LoadFont(Fonts.VgaFont8, 0x100, 0, State.BL, 8);
                _vgaFunctions.SetScanLines(8);
                break;
            case 0x14:
                _vgaFunctions.LoadFont(Fonts.VgaFont16, 0x100, 0, State.BL, 16);
                _vgaFunctions.SetScanLines(16);
                break;
            case 0x20:
                _vgaFunctions.LoadUserCharacters8X8(State.ES, State.BP);
                break;
            case 0x21:
                _vgaFunctions.LoadUserGraphicsCharacters(State.ES, State.BP, State.CL, State.BL, State.DL);
                break;
            case 0x22:
                _vgaFunctions.LoadRom8X14Font(State.BL, State.DL);
                break;
            case 0x23:
                _vgaFunctions.LoadRom8X8Font(State.BL, State.DL);
                break;
            case 0x24:
                _vgaFunctions.LoadGraphicsRom8X16Font(State.BL, State.DL);
                break;
            case 0x30:
                SegmentedAddress address = _vgaFunctions.GetFontAddress(State.BH);
                State.ES = address.Segment;
                State.BP = address.Offset;
                State.CX = (ushort)(_biosDataArea.CharacterHeight & 0xFF);
                State.DL = _biosDataArea.ScreenRows;
                break;

            default:
                throw new NotSupportedException($"AL=0x{State.AL:X2} is not a valid subFunction for INT 10 11");
        }
    }

    /// <inheritdoc />
    public void SetPaletteRegisters() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 10 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(SetPaletteRegisters), State.AL);
        }
        switch (State.AL) {
            case 0x00:
                _vgaFunctions.SetEgaPaletteRegister(State.BL, State.BH);
                break;
            case 0x01:
                _vgaFunctions.SetOverscanBorderColor(State.BH);
                break;
            case 0x02:
                _vgaFunctions.SetAllPaletteRegisters(State.ES, State.DX);
                break;
            case 0x03:
                _vgaFunctions.ToggleIntensity((State.BL & BiosConstants.UpdateCursorPositionFlag) != 0);
                break;
            case 0x07:
                if (State.BL > BiosConstants.MaxPaletteRegister) {
                    return;
                }
                State.BH = _vgaFunctions.ReadPaletteRegister(State.BL);
                break;
            case 0x08:
                State.BH = _vgaFunctions.GetOverscanBorderColor();
                break;
            case 0x09:
                _vgaFunctions.GetAllPaletteRegisters(State.ES, State.DX);
                break;
            case 0x10:
                _vgaFunctions.WriteToDac(State.BL, State.DH, State.CH, State.CL);
                break;
            case 0x12:
                _vgaFunctions.WriteToDac(State.ES, State.DX, State.BL, State.CX);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 10 {MethodName} - set block of DAC color registers. {Amount} colors starting at register {StartRegister}, source address: {Segment:X4}:{Offset:X4}",
                        nameof(VgaBios), nameof(SetPaletteRegisters), State.BL, State.CX, State.ES, State.DX);
                }
                break;
            case 0x13:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 10 {MethodName} - Select color page, mode '{Mode}', value 0x{Value:X2} ",
                        nameof(VgaBios), nameof(SetPaletteRegisters), State.BL == 0 ? "set Mode Control register bit 7" : "set color select register", State.BH);
                }
                if (State.BL == 0) {
                    _vgaFunctions.SetP5P4Select((State.BH & BiosConstants.UpdateCursorPositionFlag) != 0);
                } else {
                    _vgaFunctions.SetColorSelectRegister(State.BH);
                }
                break;
            case 0x15:
                byte[] rgb = _vgaFunctions.ReadFromDac((byte)State.BX, 1);
                State.DH = rgb[0];
                State.CH = rgb[1];
                State.CL = rgb[2];
                break;
            case 0x17:
                _vgaFunctions.ReadFromDac(State.ES, State.DX, (byte)State.BX, State.CX);
                break;
            case 0x18:
                _vgaFunctions.WriteToPixelMask(State.BL);
                break;
            case 0x19:
                State.BL = _vgaFunctions.ReadPixelMask();
                break;
            case 0x1A:
                State.BX = _vgaFunctions.ReadColorPageState();
                break;
            case 0x1B:
                _vgaFunctions.PerformGrayScaleSumming(State.BL, State.CX);
                break;
            default:
                throw new NotSupportedException($"0x{State.AL:X2} is not a valid palette register subFunction");
        }
    }

    /// <inheritdoc />
    public void GetVideoState() {
        State.BH = _biosDataArea.CurrentVideoPage;
        State.AL = (byte)(_biosDataArea.VideoMode | _biosDataArea.VideoCtl & BiosConstants.VideoControlBitMask);
        State.AH = (byte)_biosDataArea.ScreenColumns;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0F {MethodName} - Page {Page}, mode {Mode}, columns {Columns}",
                nameof(VgaBios), nameof(GetVideoState), State.BH, State.AL, State.AH);
        }
    }

    /// <inheritdoc />
    public void WriteTextInTeletypeMode() {
        CharacterPlusAttribute characterPlusAttribute = new((char)State.AL, State.BL, false);
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(_biosDataArea.CurrentVideoPage);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0E {MethodName} - {Character} Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteTextInTeletypeMode), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        _vgaFunctions.WriteTextInTeletypeMode(characterPlusAttribute);
    }

    /// <inheritdoc />
    public void SetColorPaletteOrBackGroundColor() {
        if (State.BH == 0x00) {
            _vgaFunctions.SetBorderColor(State.BL);
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("{ClassName} INT 10 0B {MethodName} - Set border color {Color}",
                    nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), State.BL);
            }
        }
        else
        {
            // Most interrupt manuals say that BH can only be 0 and 1. De facto, any nonzero
            // value in BH should be handled as 1, because this was the behaviour of the IBM
            // BIOS:
            // https://github.com/gawlas/IBM-PC-BIOS/blob/master/IBM%20PC/PCBIOSV3.ASM#L3814

            _vgaFunctions.SetPalette(State.BL);
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("{ClassName} INT 10 0B {MethodName} - Set palette id {PaletteId}",
                    nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), State.BL);
            }
        }
    }

    /// <inheritdoc />
    public void WriteCharacterAtCursor() {
        CharacterPlusAttribute characterPlusAttribute = new((char)State.AL, State.BL, false);
        byte currentVideoPage = State.BH;
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(currentVideoPage);
        int count = State.CX;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0A {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }

        _vgaFunctions.WriteCharacterAtCursor(characterPlusAttribute, currentVideoPage, count);
    }

    /// <inheritdoc />
    public void WriteCharacterAndAttributeAtCursor() {
        CharacterPlusAttribute characterPlusAttribute = new((char)State.AL, State.BL, true);
        byte currentVideoPage = State.BH;
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(currentVideoPage);
        int count = State.CX;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 09 {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAndAttributeAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        _vgaFunctions.WriteCharacterAtCursor(characterPlusAttribute, currentVideoPage, count);
    }

    /// <inheritdoc />
    public void ReadCharacterAndAttributeAtCursor() {
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(State.BH);
        CharacterPlusAttribute characterPlusAttribute = _vgaFunctions.ReadChar(cursorPosition);
        State.AL = (byte)characterPlusAttribute.Character;
        State.AH = characterPlusAttribute.Attribute;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 08 {MethodName} - Character '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(ReadCharacterAndAttributeAtCursor), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
    }

    /// <inheritdoc />
    public void ScrollPageDown() {
        _vgaFunctions.VerifyScroll(-1, State.CL, State.CH, State.DL, State.DH, State.AL, State.BH);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 07 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageDown), State.CL, State.CH, State.DL, State.DH, State.AL, State.BH);
        }
    }

    /// <inheritdoc />
    public void ScrollPageUp() {
        _vgaFunctions.VerifyScroll(1, State.CL, State.CH, State.DL, State.DH, State.AL, State.BH);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 06 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageUp), State.CL, State.CH, State.DL, State.DH, State.AL, State.BH);
        }
    }

    /// <inheritdoc />
    public void SelectActiveDisplayPage() {
        byte page = State.AL;
        int address = _vgaFunctions.SetActivePage(page);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 05 {MethodName} - page {Page}, address {Address:X4}",
                nameof(VgaBios), nameof(SelectActiveDisplayPage), page, address);
        }
    }

    /// <inheritdoc />
    public void GetCursorPosition() {
        byte page = State.BH;
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(page);
        State.CX = _biosDataArea.CursorType;
        State.DL = (byte)cursorPosition.X;
        State.DH = (byte)cursorPosition.Y;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 03 {MethodName} - cursor position {X},{Y} on page {Page} type {Type}",
                nameof(VgaBios), nameof(GetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page, State.CX);
        }
    }

    /// <inheritdoc />
    public void SetCursorPosition() {
        CursorPosition cursorPosition = new(State.DL, State.DH, State.BH);
        _vgaFunctions.SetCursorPosition(cursorPosition);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 02 {MethodName} - cursor position {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(SetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
    }

    /// <inheritdoc />
    public void SetCursorType() {
        _vgaFunctions.SetCursorShape(State.CX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 01 {MethodName} - CX: {CX}",
                nameof(VgaBios), nameof(SetCursorType), State.CX);
        }
    }

    /// <inheritdoc />
    public void ReadDot() {
        State.AL = _vgaFunctions.ReadPixel(State.CX, State.DX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0D {MethodName} - pixel at {X},{Y} = 0x{Pixel:X2}",
                nameof(VgaBios), nameof(ReadDot), State.CX, State.DX, State.AL);
        }
    }

    /// <inheritdoc />
    public void WriteDot() {
        _vgaFunctions.WritePixel(State.AL, State.CX, State.DX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0C {MethodName} - write {Pixel} at {X},{Y}",
                nameof(VgaBios), nameof(WriteDot), State.AL, State.CX, State.DX);
        }
    }

    /// <inheritdoc />
    public void SetVideoMode() {
        int modeId = State.AL & BiosConstants.VideoModeMask;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_biosDataArea.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if ((State.AL & BiosConstants.DontClearMemoryFlag) != 0) {
            flags |= ModeFlags.NoClearMem;
        }

        // Set AL
        if (modeId > 7) {
            State.AL = BiosConstants.ModeAbove7Return;
        } else if (modeId == 6) {
            State.AL = BiosConstants.Mode6Return;
        } else {
            State.AL = BiosConstants.ModeBelow7Return;
        }
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 00 {MethodName} - mode {ModeId:X2}, {Flags}",
                nameof(VgaBios), nameof(SetVideoMode), modeId, flags);
        }
        _vgaFunctions.VgaSetMode(modeId, flags);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("VGA BIOS mode set complete");
        }
    }

    /// <summary>
    ///     Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = State.AH;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} running INT 10 operation 0x{Operation:X2}", nameof(VgaBios), operation);
        }
        if (!HasRunnable(operation) && LoggerService.IsEnabled(LogEventLevel.Error)) {
            LoggerService.Error("INT10H: Unrecognized VgaBios function number in AH register: {OperationNumber}", State.AH);
        }
        Run(operation);
    }

    /// <inheritdoc />
    public void ReadLightPenPosition() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 04 {MethodName} - Read Light Pen Position, returning 0",
                nameof(VgaBios), nameof(ReadLightPenPosition));
        }
        State.AX = State.BX = State.CX = State.DX = 0;
    }

    private void InitializeBiosArea() {
        // init detected hardware BIOS Area
        // set 80x25 color (not clear from RBIL but usual)
        _biosDataArea.EquipmentListFlags = (ushort)(_biosDataArea.EquipmentListFlags & ~BiosConstants.EquipmentListFlagsMask | BiosConstants.VideoControl80x25Color);

        // Set the basic modeset options
        _biosDataArea.ModesetCtl = BiosConstants.DefaultModeSetControl;
        _biosDataArea.DisplayCombinationCode = BiosConstants.DefaultDisplayCombinationCode;
    }

    private void VideoScreenOnOff() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 36 {MethodName} - Ignored",
                nameof(VgaBios), nameof(VideoScreenOnOff));
        }
        State.AL = BiosConstants.SubfunctionSuccess;
    }

    private void DisplaySwitch() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 35 {MethodName} - Ignored",
                nameof(VgaBios), nameof(DisplaySwitch));
        }
        State.AL = BiosConstants.SubfunctionSuccess;
    }

    private void CursorEmulation() {
        bool enabled = (State.AL & BiosConstants.UpdateCursorPositionFlag) == 0;
        _vgaFunctions.CursorEmulation(enabled);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 34 {MethodName} - {Result}",
                nameof(VgaBios), nameof(CursorEmulation), enabled ? "Enabled" : "Disabled");
        }
        State.AL = BiosConstants.SubfunctionSuccess;
    }

    private void SummingToGrayScales() {
        bool enabled = (State.AL & BiosConstants.UpdateCursorPositionFlag) == 0;
        _vgaFunctions.SummingToGrayScales(enabled);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 33 {MethodName} - {Result}",
                nameof(VgaBios), nameof(SummingToGrayScales), enabled ? "Enabled" : "Disabled");
        }
        State.AL = BiosConstants.SubfunctionSuccess;
    }

    private void VideoEnableDisable() {
        _vgaFunctions.EnableVideoAddressing((State.AL & BiosConstants.UpdateCursorPositionFlag) == 0);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 32 {MethodName} - {Result}",
                nameof(VgaBios), nameof(VideoEnableDisable), (State.AL & BiosConstants.UpdateCursorPositionFlag) == 0 ? "Enabled" : "Disabled");
        }
        State.AL = BiosConstants.SubfunctionSuccess;
    }

    private void DefaultPaletteLoading() {
        _vgaFunctions.DefaultPaletteLoading((State.AL & BiosConstants.UpdateCursorPositionFlag) != 0);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 31 {MethodName} - 0x{Al:X2}",
                nameof(VgaBios), nameof(DefaultPaletteLoading), State.AL);
        }
        State.AL = BiosConstants.SubfunctionSuccess;
    }

    private void SelectScanLines() {
        int lines = State.AL switch {
            0x00 => BiosConstants.ScanLines200,
            0x01 => BiosConstants.ScanLines350,
            0x02 => BiosConstants.ScanLines400,
            _ => throw new NotSupportedException($"AL=0x{State.AL:X2} is not a valid subFunction for INT 10 12 30")
        };
        _vgaFunctions.SelectScanLines(lines);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 30 {MethodName} - {Lines} lines",
                nameof(VgaBios), nameof(SelectScanLines), lines);
        }
        State.AL = BiosConstants.SubfunctionSuccess;
    }

    private void EgaVgaInformation() {
        State.BH = (byte)(_vgaFunctions.GetColorMode() ? BiosConstants.ColorModeMemory : 0x00);
        State.BL = BiosConstants.Memory256KB;
        State.CX = _vgaFunctions.GetFeatureSwitches();
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 10 {MethodName} - ColorMode 0x{ColorMode:X2}, Memory: 0x{Memory:X2}, FeatureSwitches: 0x{FeatureSwitches:X2}",
                nameof(VgaBios), nameof(EgaVgaInformation), State.BH, State.BL, State.CX);
        }
    }

    private void SetBlockSpecifier(byte fontBlock) {
        _vgaFunctions.SetFontBlockSpecifier(fontBlock);
    }

    private void FillDispatchTable() {
        AddAction(0x00, SetVideoMode);
        AddAction(0x01, SetCursorType);
        AddAction(0x02, SetCursorPosition);
        AddAction(0x03, GetCursorPosition);
        AddAction(0x04, ReadLightPenPosition);
        AddAction(0x05, SelectActiveDisplayPage);
        AddAction(0x06, ScrollPageUp);
        AddAction(0x07, ScrollPageDown);
        AddAction(0x08, ReadCharacterAndAttributeAtCursor);
        AddAction(0x09, WriteCharacterAndAttributeAtCursor);
        AddAction(0x0A, WriteCharacterAtCursor);
        AddAction(0x0B, SetColorPaletteOrBackGroundColor);
        AddAction(0x0C, WriteDot);
        AddAction(0x0D, ReadDot);
        AddAction(0x0E, WriteTextInTeletypeMode);
        AddAction(0x0F, GetVideoState);
        AddAction(0x10, SetPaletteRegisters);
        AddAction(0x11, LoadFontInfo);
        AddAction(0x12, VideoSubsystemConfiguration);
        AddAction(0x13, WriteString);
        AddAction(0x1A, GetSetDisplayCombinationCode);
        AddAction(0x1B, () => GetFunctionalityInfo());
        AddAction(0x4F, VesaFunctions);
    }

    /// <summary>
    /// VESA VBE 1.0 function dispatcher (INT 10h AH=4Fh).
    /// Dispatches to specific VBE functions based on AL subfunction.
    /// </summary>
    public void VesaFunctions() {
        byte subfunction = State.AL;
        switch ((VbeFunction)subfunction) {
            case VbeFunction.GetControllerInfo:
                VbeGetControllerInfo();
                break;
            case VbeFunction.GetModeInfo:
                VbeGetModeInfo();
                break;
            case VbeFunction.SetMode:
                VbeSetMode();
                break;
            default:
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning("{ClassName} INT 10 4F{Subfunction:X2} - Unsupported VBE function",
                        nameof(VgaBios), subfunction);
                }
                State.AX = (ushort)VbeStatus.Failed;
                break;
        }
    }

    /// <inheritdoc cref="IVesaBiosExtension.VbeGetControllerInfo"/>
    public void VbeGetControllerInfo() {
        ushort segment = State.ES;
        ushort offset = State.DI;
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);

        var vbeInfo = new VbeInfoBlock(Memory, address);

        // Fill VBE Info Block (VBE 1.0)
        vbeInfo.Signature = "VESA";
        vbeInfo.Version = VbeConstants.Version10;

        // OEM String pointer - point to a location beyond the main structure
        vbeInfo.OemStringOffset = (ushort)(offset + VbeConstants.OemStringOffset);
        vbeInfo.OemStringSegment = segment;

        // Capabilities: DAC is switchable, controller is VGA compatible
        vbeInfo.Capabilities = VbeConstants.DacSwitchableCapability;

        // Video Mode List pointer - point after OEM string
        vbeInfo.VideoModeListOffset = (ushort)(offset + VbeConstants.ModeListOffset);
        vbeInfo.VideoModeListSegment = segment;

        // Total Memory in 64KB blocks (1MB = 16 blocks)
        vbeInfo.TotalMemory = VbeConstants.TotalMemory1MB;

        // Write OEM String at offset+256
        vbeInfo.WriteOemString(VbeConstants.OemString, VbeConstants.OemStringOffset);

        // Write Video Mode List at offset+280
        ushort[] vesaModes = { VbeConstants.VesaMode800x600x16 };
        vbeInfo.WriteModeList(vesaModes, VbeConstants.ModeListOffset);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 4F00 VbeGetControllerInfo - Returning VBE 1.0 info at {Segment:X4}:{Offset:X4}",
                nameof(VgaBios), segment, offset);
        }

        State.AX = (ushort)VbeStatus.Success;
    }


    /// <inheritdoc cref="IVesaBiosExtension.VbeGetModeInfo"/>
    public void VbeGetModeInfo() {
        ushort modeNumber = State.CX;
        ushort segment = State.ES;
        ushort offset = State.DI;
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);

        // Get mode parameters based on VESA mode number
        (ushort width, ushort height, byte bpp, bool supported) = GetVesaModeParams(modeNumber);

        if (!supported) {
            if (_logger.IsEnabled(LogEventLevel.Warning)) {
                _logger.Warning("{ClassName} INT 10 4F01 VbeGetModeInfo - Unsupported mode 0x{Mode:X4}",
                    nameof(VgaBios), modeNumber);
            }
            State.AX = (ushort)VbeStatus.Failed;
            return;
        }

        var modeInfo = new VbeModeInfoBlock(Memory, address);
        modeInfo.Clear();

        // Mode Attributes
        modeInfo.ModeAttributes = VbeModeInfoConstants.ModeAttributesSupported;

        // Window attributes
        modeInfo.WindowAAttributes = VbeModeInfoConstants.WindowAttributesReadWriteSupported;
        modeInfo.WindowBAttributes = VbeModeInfoConstants.WindowAttributesNotSupported;
        modeInfo.WindowGranularity = VbeModeInfoConstants.WindowGranularity64KB;
        modeInfo.WindowSize = VbeModeInfoConstants.WindowSize64KB;
        modeInfo.WindowASegment = VbeModeInfoConstants.WindowASegmentAddress;
        modeInfo.WindowBSegment = VbeModeInfoConstants.WindowBSegmentAddress;
        modeInfo.WindowFunctionOffset = 0;
        modeInfo.WindowFunctionSegment = 0;

        // Calculate bytes per scan line
        ushort bytesPerLine;
        if (bpp == 4) {
            bytesPerLine = (ushort)(width / 8); // 4-bit planar
        } else if (bpp == 1) {
            bytesPerLine = (ushort)(width / 8);
        } else if (bpp == 15 || bpp == 16) {
            bytesPerLine = (ushort)(width * 2);
        } else if (bpp == 24) {
            bytesPerLine = (ushort)(width * 3);
        } else if (bpp == 32) {
            bytesPerLine = (ushort)(width * 4);
        } else {
            bytesPerLine = width; // 8-bit packed pixel
        }
        modeInfo.BytesPerScanLine = bytesPerLine;

        // Resolution and character info
        modeInfo.XResolution = width;
        modeInfo.YResolution = height;
        modeInfo.XCharSize = VbeModeInfoConstants.CharWidth;
        modeInfo.YCharSize = VbeModeInfoConstants.CharHeight;
        modeInfo.NumberOfPlanes = (byte)(bpp == 4 ? 4 : 1);
        modeInfo.BitsPerPixel = bpp;
        modeInfo.NumberOfBanks = VbeModeInfoConstants.SingleBank;

        // Memory model
        byte memoryModel = bpp switch {
            4 => VbeModeInfoConstants.MemoryModelPlanar,
            8 => VbeModeInfoConstants.MemoryModelPackedPixel,
            15 => VbeModeInfoConstants.MemoryModelDirectColor,
            16 => VbeModeInfoConstants.MemoryModelDirectColor,
            24 => VbeModeInfoConstants.MemoryModelDirectColor,
            32 => VbeModeInfoConstants.MemoryModelDirectColor,
            _ => VbeModeInfoConstants.MemoryModelPackedPixel
        };
        modeInfo.MemoryModel = memoryModel;
        modeInfo.BankSize = VbeModeInfoConstants.BankSize64KB;
        modeInfo.NumberOfImagePages = VbeModeInfoConstants.NoImagePages;
        modeInfo.Reserved1 = VbeModeInfoConstants.Reserved;

        // Direct color fields for high-color/true-color modes
        if (bpp >= 15) {
            if (bpp == 15 || bpp == 16) {
                modeInfo.RedMaskSize = VbeModeInfoConstants.RedGreenBlueMaskSize;
                modeInfo.RedFieldPosition = (byte)(bpp == 16 ? 11 : 10);
                modeInfo.GreenMaskSize = (byte)(bpp == 16 ? VbeModeInfoConstants.GreenMaskSize6Bit : VbeModeInfoConstants.RedGreenBlueMaskSize);
                modeInfo.GreenFieldPosition = 5;
                modeInfo.BlueMaskSize = VbeModeInfoConstants.RedGreenBlueMaskSize;
                modeInfo.BlueFieldPosition = 0;
            } else if (bpp == 24 || bpp == 32) {
                modeInfo.RedMaskSize = VbeModeInfoConstants.RedGreenBlueMaskSize8;
                modeInfo.RedFieldPosition = 16;
                modeInfo.GreenMaskSize = VbeModeInfoConstants.RedGreenBlueMaskSize8;
                modeInfo.GreenFieldPosition = 8;
                modeInfo.BlueMaskSize = VbeModeInfoConstants.RedGreenBlueMaskSize8;
                modeInfo.BlueFieldPosition = 0;
            }
        }

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 4F01 VbeGetModeInfo - Mode 0x{Mode:X4}: {Width}x{Height}x{Bpp}",
                nameof(VgaBios), modeNumber, width, height, bpp);
        }

        // Return success
        State.AX = 0x004F;
    }

    /// <inheritdoc cref="IVesaBiosExtension.VbeSetMode"/>
    public void VbeSetMode() {
        ushort modeNumber = State.BX;
        // VBE 1.0/1.2 does not support LFB; bit 14 is ignored (banked mode is always used)
        bool dontClearDisplay = (modeNumber & (ushort)VbeModeFlags.DontClearMemory) != 0;
        ushort mode = (ushort)(modeNumber & (ushort)VbeModeFlags.ModeNumberMask);

        // Map VESA mode to internal mode
        int? internalMode = MapVesaModeToInternal(mode);

        if (!internalMode.HasValue) {
            if (_logger.IsEnabled(LogEventLevel.Warning)) {
                _logger.Warning("{ClassName} INT 10 4F02 VbeSetMode - Unsupported mode 0x{Mode:X4}",
                    nameof(VgaBios), mode);
            }
            State.AX = (ushort)VbeStatus.Failed;
            return;
        }

        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_biosDataArea.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if (dontClearDisplay) {
            flags |= ModeFlags.NoClearMem;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 4F02 VbeSetMode - Setting VESA mode 0x{VesaMode:X4} (internal mode 0x{InternalMode:X2})",
                nameof(VgaBios), mode, internalMode.Value);
        }

        _vgaFunctions.VgaSetMode(internalMode.Value, flags);

        State.AX = (ushort)VbeStatus.Success;
    }

    /// <summary>
    /// Gets the parameters for a VESA mode number.
    /// Returns mode information for all standard VESA modes defined in VBE 1.2 spec.
    /// This is used by VbeGetModeInfo (VBE 01h) to return mode characteristics.
    /// Note: supported=true means mode info is available for queries (per VBE spec);
    /// actual ability to SET the mode depends on MapVesaModeToInternal returning a valid internal mode.
    /// Programs query mode info before setting modes to check if they're suitable.
    /// </summary>
    private static (ushort width, ushort height, byte bpp, bool supported) GetVesaModeParams(ushort mode) {
        return mode switch {
            // VBE 1.2 standard modes - return info for queries
            // Note: Only mode 0x102 can actually be SET (has internal VGA mode support)
            0x100 => (640, 400, 8, true),    // 640x400x256
            0x101 => (640, 480, 8, true),    // 640x480x256
            0x102 => (800, 600, 4, true),    // 800x600x16 (planar) - CAN BE SET via mode 0x6A
            0x103 => (800, 600, 8, true),    // 800x600x256
            0x104 => (1024, 768, 4, true),   // 1024x768x16 (planar)
            0x105 => (1024, 768, 8, true),   // 1024x768x256
            0x106 => (1280, 1024, 4, true),  // 1280x1024x16 (planar)
            0x107 => (1280, 1024, 8, true),  // 1280x1024x256
            0x10D => (320, 200, 15, true),   // 320x200x15-bit
            0x10E => (320, 200, 16, true),   // 320x200x16-bit
            0x10F => (320, 200, 24, true),   // 320x200x24-bit
            0x110 => (640, 480, 15, true),   // 640x480x15-bit (S3 mode 0x70)
            0x111 => (640, 480, 16, true),   // 640x480x16-bit
            0x112 => (640, 480, 24, true),   // 640x480x24-bit
            0x113 => (800, 600, 15, true),   // 800x600x15-bit
            0x114 => (800, 600, 16, true),   // 800x600x16-bit
            0x115 => (800, 600, 24, true),   // 800x600x24-bit
            0x116 => (1024, 768, 15, true),  // 1024x768x15-bit
            0x117 => (1024, 768, 16, true),  // 1024x768x16-bit
            0x118 => (1024, 768, 24, true),  // 1024x768x24-bit
            0x119 => (1280, 1024, 15, true), // 1280x1024x15-bit
            0x11A => (1280, 1024, 16, true), // 1280x1024x16-bit
            0x11B => (1280, 1024, 24, true), // 1280x1024x24-bit
            _ => (0, 0, 0, false)
        };
    }

    /// <summary>
    /// Maps a VESA mode number to an internal VGA mode number.
    /// Returns null if the mode is not supported by the emulator's VGA hardware.
    /// Per VBE 1.2 spec, only modes with actual hardware support should be settable.
    /// </summary>
    private static int? MapVesaModeToInternal(ushort vesaMode) {
        // Only map modes that have actual internal VGA mode support
        // The emulator currently only supports standard VGA modes + mode 0x6A (800x600x16)
        // High-color/true-color modes and higher resolutions require SVGA hardware
        // that is not emulated, so they return null (unsupported)
        return vesaMode switch {
            VbeConstants.VesaMode800x600x16 => VbeConstants.InternalMode800x600x16,
            // All other VESA modes are not supported by the current VGA emulation
            _ => null
        };
    }

    /// <inheritdoc />
    public VideoFunctionalityInfo GetFunctionalityInfo() {
        ushort segment = State.ES;
        ushort offset = State.DI;

        switch (State.BX) {
            case 0x0:
                State.AL = BiosConstants.FunctionSupported;
                break;
            default:
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning("{ClassName} INT 10 1B: {MethodName} - Unsupported subFunction 0x{SubFunction:X2}",
                        nameof(VgaBios), nameof(GetFunctionalityInfo), State.BX);
                }
                State.AL = 0;
                break;
        }

        VgaMode currentMode = _vgaFunctions.GetCurrentMode();
        ushort cursorType = _biosDataArea.CursorType;
        byte characterMapRegister = _vgaFunctions.GetCharacterMapSelectRegister();
        (byte primaryCharacterTable, byte secondaryCharacterTable) = DecodeCharacterMapSelections(characterMapRegister);

        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
        var info = new VideoFunctionalityInfo(Memory, address) {
            SftAddress = MemoryMap.StaticFunctionalityTableSegment << 16,
            VideoMode = _biosDataArea.VideoMode,
            ScreenColumns = _biosDataArea.ScreenColumns,
            VideoBufferLength = _biosDataArea.VideoPageSize,
            VideoBufferAddress = _biosDataArea.VideoPageStart,
            CursorEndLine = ExtractCursorEndLine(cursorType),
            CursorStartLine = ExtractCursorStartLine(cursorType),
            ActiveDisplayPage = _biosDataArea.CurrentVideoPage,
            CrtControllerBaseAddress = _biosDataArea.CrtControllerBaseAddress,
            CurrentRegister3X8Value = 0,
            CurrentRegister3X9Value = 0,
            ScreenRows = _biosDataArea.ScreenRows,
            CharacterMatrixHeight = _biosDataArea.CharacterHeight,
            ActiveDisplayCombinationCode = _biosDataArea.DisplayCombinationCode,
            AlternateDisplayCombinationCode = BiosConstants.NoSecondaryDisplay,
            NumberOfColorsSupported = CalculateColorCount(currentMode),
            NumberOfPages = CalculatePageCount(currentMode),
            NumberOfActiveScanLines = CalculateScanLineCode(currentMode),
            TextCharacterTableUsed = primaryCharacterTable,
            TextCharacterTableUsed2 = secondaryCharacterTable,
            OtherStateInformation = GetOtherStateInformation(currentMode),
            VideoRamAvailable = BiosConstants.Memory256KB,
            SaveAreaStatus = 0
        };

        for (byte i = 0; i < 8; i++) {
            CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(i);
            info.SetCursorPosition(i, (byte)cursorPosition.X, (byte)cursorPosition.Y);
        }

        if (_logger.IsEnabled(LogEventLevel.Warning)) {
            _logger.Warning("{ClassName} INT 10 1B: {MethodName} - experimental! {@Summary}",
                nameof(VgaBios), nameof(GetFunctionalityInfo), info.CreateFunctionalityInfoLogSnapshot());
        }
        return info;
    }

    /// <summary>
    /// Writes values to the static functionality table in emulated memory.
    /// </summary>
    private void InitializeStaticFunctionalityTable() {
        Memory.UInt32[MemoryMap.StaticFunctionalityTableSegment, 0] = BiosConstants.StaticFunctionalityAllModes;
        Memory.UInt8[MemoryMap.StaticFunctionalityTableSegment, 0x07] = BiosConstants.StaticFunctionalityAllScanLines;
    }

    private static byte ExtractCursorStartLine(ushort cursorType) {
        return (byte)((cursorType >> 8) & BiosConstants.CursorTypeMask);
    }

    private static byte ExtractCursorEndLine(ushort cursorType) {
        return (byte)(cursorType & BiosConstants.CursorEndMask);
    }

    private static (byte Primary, byte Secondary) DecodeCharacterMapSelections(byte registerValue) {
        byte primary = (byte)((((registerValue >> 5) & 0x01) << 2) | (registerValue & 0x03));
        byte secondary = (byte)((((registerValue >> 4) & 0x01) << 2) | ((registerValue >> 2) & 0x03));
        return (primary, secondary);
    }

    private static byte GetOtherStateInformation(VgaMode mode) {
        return mode.MemoryModel == MemoryModel.Text ? (byte)0x21 : (byte)0x01;
    }

    private static ushort CalculateColorCount(VgaMode mode) {
        return mode.MemoryModel switch {
            MemoryModel.Text => (ushort)(mode.StartSegment == VgaConstants.MonochromeTextSegment ? 1 : 16),
            MemoryModel.Cga => CalculateColorCountFromBits(mode.BitsPerPixel),
            MemoryModel.Planar => mode.BitsPerPixel switch {
                4 => 16,
                1 => 2,
                _ => CalculateColorCountFromBits(mode.BitsPerPixel)
            },
            _ => CalculateColorCountFromBits(mode.BitsPerPixel)
        };
    }

    private static ushort CalculateColorCountFromBits(byte bitsPerPixel) {
        int bits = bitsPerPixel <= 0 ? 1 : bitsPerPixel;
        return (ushort)(1 << bits);
    }

    private byte CalculatePageCount(VgaMode mode) {
        return mode.MemoryModel switch {
            MemoryModel.Text => CalculateTextPageCount(),
            MemoryModel.Cga => CalculateGraphicsPageCount(16 * 1024, CalculatePackedPageSize(mode)),
            MemoryModel.Planar => CalculateGraphicsPageCount(64 * 1024, CalculatePlanarPageSize(mode)),
            _ => CalculateGraphicsPageCount(64 * 1024, CalculatePackedPageSize(mode))
        };
    }

    private byte CalculateTextPageCount() {
        int pageSize = _biosDataArea.VideoPageSize;
        if (pageSize <= 0) {
            return 1;
        }

        int count = 32 * 1024 / pageSize;
        count = count switch {
            0 => 1,
            > 8 => 8,
            _ => count
        };
        return (byte)count;
    }

    private static byte CalculateGraphicsPageCount(int windowSize, int pageSize) {
        if (pageSize <= 0) {
            pageSize = 1;
        }

        int count = windowSize / pageSize;
        if (count == 0) {
            count = 1;
        }

        return (byte)count;
    }

    private static int CalculatePlanarPageSize(VgaMode mode) {
        int bytesPerPlane = mode.Width * mode.Height / 8;
        return bytesPerPlane <= 0 ? 1 : bytesPerPlane;
    }

    private static int CalculatePackedPageSize(VgaMode mode) {
        int bitsPerPixel = mode.BitsPerPixel <= 0 ? 1 : mode.BitsPerPixel;
        int bytes = mode.Width * mode.Height * bitsPerPixel / 8;
        return bytes <= 0 ? 1 : bytes;
    }

    private byte CalculateScanLineCode(VgaMode mode) {
        int scanLines;
        if (mode.MemoryModel == MemoryModel.Text) {
            int rows = _biosDataArea.ScreenRows + 1;
            scanLines = rows * _biosDataArea.CharacterHeight;
        } else {
            scanLines = mode.Height;
        }

        return scanLines switch {
            >= 480 => 3,
            >= 400 => 2,
            >= 350 => 1,
            _ => 0
        };
    }
}