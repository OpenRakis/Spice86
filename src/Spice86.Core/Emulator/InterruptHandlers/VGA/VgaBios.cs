using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
///     A VGA BIOS implementation.
/// </summary>
public class VgaBios : InterruptHandler, IVideoInt10Handler, IVesaBiosExtension {
    private readonly BiosDataArea _biosDataArea;
    private readonly ILogger _logger;
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
        /// VBE 1.2 version number in BCD format (major.minor = 0x0102 = 1.2).
        /// </summary>
        public const ushort Version12 = 0x0102;

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
        /// Total memory: 256KB = 4 blocks of 64KB each.
        /// </summary>
        public const ushort TotalMemory256KB = 4;

        /// <summary>
        /// OEM identification string for Spice86 VBE implementation.
        /// </summary>
        public const string OemString = "Spice86 VBE";

        /// <summary>
        /// Segment for VBE 00h auxiliary data (OEM string and mode list).
        /// </summary>
        public const ushort VbeDataSegment = MemoryMap.StaticFunctionalityTableSegment;

        /// <summary>
        /// Offset where OEM string is written in VBE BIOS-owned storage.
        /// </summary>
        public const ushort OemStringOffset = 0x0100;

        /// <summary>
        /// Offset where the video mode list is written in VBE BIOS-owned storage.
        /// </summary>
        public const ushort ModeListOffset = 0x0200;

        /// <summary>
        /// VBE controller info block size in bytes.
        /// </summary>
        public const int ControllerInfoBlockSize = 256;

        /// <summary>
        /// VBE mode info block size in bytes.
        /// </summary>
        public const int ModeInfoBlockSize = 256;

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
        public const byte FunctionalityInfoSupported = 0x1B;
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
        public const byte Bit0Mask = 0x01;
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
    public VgaBios(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, IVgaFunctionality vgaFunctions, BiosDataArea biosDataArea, ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _vgaFunctions = vgaFunctions;
        _logger = loggerService;
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("Initializing VGA BIOS");
        }
        InitializeBiosArea();
        InitializeStaticFunctionalityTable();
    }

    /// <summary>
    ///     The interrupt vector this class handles.
    /// </summary>
    public override byte VectorNumber => 0x10;

    /// <inheritdoc />
    [InterruptService(0x13, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Es | CpuValues.Bp, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteString() {
        WriteString(State.AL, State.CX, State.DX, State.BX, State.BP, State.ES);
    }

    /// <summary>
    ///     Writes the string at <paramref name="es"/>:<paramref name="bp"/> on page BH at row/column
    ///     DH/DL, with mode selected by <paramref name="al"/>.
    /// </summary>
    public void WriteString(byte al, ushort cx, ushort dx, ushort bx, ushort bp, ushort es) {
        CursorPosition cursorPosition = new((byte)dx, (byte)(dx >> 8), (byte)(bx >> 8));
        ushort length = cx;
        ushort segment = es;
        ushort offset = bp;
        byte attribute = (byte)bx;
        bool includeAttributes = (al & BiosConstants.IncludeAttributesFlag) != 0;
        bool updateCursorPosition = (al & BiosConstants.UpdateCursorPositionFlag) != 0;
        if (_logger.IsEnabled(LogLevel.Debug)) {
            uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
            string str = Memory.GetZeroTerminatedString(address, cx);
            _logger.LogDebug("{ClassName} INT 10 13 {MethodName}: {String} at {X},{Y} attribute: 0x{Attribute:X2}",
                nameof(VgaBios), nameof(WriteString), str, cursorPosition.X, cursorPosition.Y, includeAttributes ? "included" : attribute);
        }
        _vgaFunctions.WriteString(segment, offset, length, includeAttributes, attribute, cursorPosition, updateCursorPosition);
    }

    /// <inheritdoc />
    [InterruptService(0x1A, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bl, Writes = CpuValues.Ax | CpuValues.Bx, Memory = ServiceMemoryEffect.ReadWrite, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetSetDisplayCombinationCode() {
        (byte al, ushort? bx) = GetSetDisplayCombinationCode(State.AL, State.BL);
        State.AL = al;
        if (bx is { } bxValue) {
            State.BX = bxValue;
        }
    }

    /// <summary>
    ///     Gets (operation 0) or sets (operation 1) the display combination code.
    ///     Returns the AL result and the complete BX on a get, null BX on a set.
    /// </summary>
    public (byte al, ushort? bx) GetSetDisplayCombinationCode(byte operation, byte displayCode) {
        switch (operation) {
            case 0x00: {
                    ushort bx = (ushort)((BiosConstants.NoSecondaryDisplay << 8) | _biosDataArea.DisplayCombinationCode);
                    if (_logger.IsEnabled(LogLevel.Debug)) {
                        _logger.LogDebug("{ClassName} INT 10 1A {MethodName} - Get: DCC 0x{Dcc:X2}",
                            nameof(VgaBios), nameof(GetSetDisplayCombinationCode), bx & 0xFF);
                    }
                    return (BiosConstants.FunctionSupported, bx);
                }
            case 0x01: {
                    _biosDataArea.DisplayCombinationCode = displayCode;
                    if (_logger.IsEnabled(LogLevel.Debug)) {
                        _logger.LogDebug("{ClassName} INT 10 1A {MethodName} - Set: DCC 0x{Dcc:X2}",
                            nameof(VgaBios), nameof(GetSetDisplayCombinationCode), displayCode);
                    }
                    return (BiosConstants.FunctionSupported, null);
                }
            default: {
                    throw new NotSupportedException($"AL=0x{operation:X2} is not a valid subFunction for INT 10 1A");
                }
        }
    }

    /// <inheritdoc />
    [InterruptService(0x12, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx, Writes = CpuValues.Al | CpuValues.Bx | CpuValues.Cx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void VideoSubsystemConfiguration() {
        (byte al, ushort? cx, ushort bx) = VideoSubsystemConfiguration(State.AL, State.BX);
        State.AL = al;
        if (cx is { } cxValue) {
            State.CX = cxValue;
        }
        State.BX = bx;
    }

    /// <summary>
    ///     Video subsystem configuration; BL selects the subfunction.
    ///     Unsupported subfunctions preserve AL and BX and return a null CX.
    /// </summary>
    public (byte al, ushort? cx, ushort bx) VideoSubsystemConfiguration(byte al, ushort bx) {
        if (_logger.IsEnabled(LogLevel.Trace)) {
            _logger.LogTrace("{ClassName} INT 10 12 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(LoadFontInfo), (byte)bx);
        }
        switch ((VideoSubsystemFunction)(byte)bx) {
            case VideoSubsystemFunction.EgaVgaInformation:
                (ushort egaCx, ushort egaBx) = EgaVgaInformation();
                return (al, egaCx, egaBx);
            case VideoSubsystemFunction.SelectScanLines:
                return (SelectScanLines(al), null, bx);
            case VideoSubsystemFunction.DefaultPaletteLoading:
                return (DefaultPaletteLoading(al), null, bx);
            case VideoSubsystemFunction.VideoEnableDisable:
                return (VideoEnableDisable(al), null, bx);
            case VideoSubsystemFunction.SummingToGrayScales:
                return (SummingToGrayScales(al), null, bx);
            case VideoSubsystemFunction.CursorEmulation:
                return (CursorEmulation(al), null, bx);
            case VideoSubsystemFunction.DisplaySwitch:
                return (DisplaySwitch(), null, bx);
            case VideoSubsystemFunction.VideoScreenOnOff:
                return (VideoScreenOnOff(), null, bx);
            default:
                // Do not fail in case the index is not valid, this is the behaviour of the VGA bios and some programs expect this (prince of persia, checkit).
                if (_logger.IsEnabled(LogLevel.Warning)) {
                    _logger.LogWarning("BL={BL} is not a valid subFunction for INT 10 12", ConvertUtils.ToHex8((byte)bx));
                }
                return (al, null, bx);
        }
    }

    /// <inheritdoc />
    [InterruptService(0x11, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Es | CpuValues.Bp, Writes = CpuValues.Cx | CpuValues.Dl | CpuValues.Es | CpuValues.Bp, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void LoadFontInfo() {
        (ushort cx, byte dl, ushort bp, ushort es) = LoadFontInfo(State.AL, State.CX, State.DX, State.BX, State.BP, State.ES);
        State.CX = cx;
        State.DL = dl;
        State.BP = bp;
        State.ES = es;
    }

    /// <summary>
    ///     Font handling; <paramref name="al"/> selects the subfunction (load, get info).
    ///     Subfunctions that produce no outputs return the passed CX, DL, BP and ES unchanged.
    /// </summary>
    public (ushort cx, byte dl, ushort bp, ushort es) LoadFontInfo(byte al, ushort cx, ushort dx, ushort bx, ushort bp, ushort es) {
        if (_logger.IsEnabled(LogLevel.Trace)) {
            _logger.LogTrace("{ClassName} INT 10 11 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(LoadFontInfo), al);
        }
        byte bl = (byte)bx;
        byte bh = (byte)(bx >> 8);
        byte dl = (byte)dx;
        switch (al) {
            case 0x00:
                _vgaFunctions.LoadUserFont(es, bp, cx, dx, bl, bh);
                break;
            case 0x01:
                _vgaFunctions.LoadFont(Fonts.VgaFont14, 0x100, 0, bl, 14);
                break;
            case 0x02:
                _vgaFunctions.LoadFont(Fonts.VgaFont8, 0x100, 0, bl, 8);
                break;
            case 0x03:
                SetBlockSpecifier(bl);
                break;
            case 0x04:
                _vgaFunctions.LoadFont(Fonts.VgaFont16, 0x100, 0, bl, 16);
                break;
            case 0x10:
                _vgaFunctions.LoadUserFont(es, bp, cx, dx, bl, bh);
                _vgaFunctions.SetScanLines(bh);
                break;
            case 0x11:
                _vgaFunctions.LoadFont(Fonts.VgaFont14, 0x100, 0, bl, 14);
                _vgaFunctions.SetScanLines(14);
                break;
            case 0x12:
                _vgaFunctions.LoadFont(Fonts.VgaFont8, 0x100, 0, bl, 8);
                _vgaFunctions.SetScanLines(8);
                break;
            case 0x14:
                _vgaFunctions.LoadFont(Fonts.VgaFont16, 0x100, 0, bl, 16);
                _vgaFunctions.SetScanLines(16);
                break;
            case 0x20:
                _vgaFunctions.LoadUserCharacters8X8(es, bp);
                break;
            case 0x21:
                _vgaFunctions.LoadUserGraphicsCharacters(es, bp, (byte)cx, bl, (byte)dx);
                break;
            case 0x22:
                _vgaFunctions.LoadRom8X14Font(bl, (byte)dx);
                break;
            case 0x23:
                _vgaFunctions.LoadRom8X8Font(bl, (byte)dx);
                break;
            case 0x24:
                _vgaFunctions.LoadGraphicsRom8X16Font(bl, (byte)dx);
                break;
            case 0x30:
                SegmentedAddress address = _vgaFunctions.GetFontAddress(bh);
                es = address.Segment;
                bp = address.Offset;
                cx = (ushort)(_biosDataArea.CharacterHeight & 0xFF);
                dl = _biosDataArea.ScreenRows;
                break;

            default:
                throw new NotSupportedException($"AL=0x{al:X2} is not a valid subFunction for INT 10 11");
        }
        return (cx, dl, bp, es);
    }

    /// <inheritdoc />
    [InterruptService(0x10, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Es, Writes = CpuValues.Bx | CpuValues.Cx | CpuValues.Dh, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetPaletteRegisters() {
        (ushort cx, byte dh, ushort bx) = SetPaletteRegisters(State.AL, State.CX, State.DX, State.BX, State.ES);
        State.CX = cx;
        State.DH = dh;
        State.BX = bx;
    }

    /// <summary>
    ///     Palette register operations; <paramref name="al"/> selects the subfunction.
    ///     Subfunctions that produce no outputs return the passed CX, DH and BX unchanged.
    /// </summary>
    public (ushort cx, byte dh, ushort bx) SetPaletteRegisters(byte al, ushort cx, ushort dx, ushort bx, ushort es) {
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 10 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(SetPaletteRegisters), al);
        }
        byte bl = (byte)bx;
        byte bh = (byte)(bx >> 8);
        byte dh = (byte)(dx >> 8);
        switch (al) {
            case 0x00:
                _vgaFunctions.SetEgaPaletteRegister(bl, bh);
                break;
            case 0x01:
                _vgaFunctions.SetOverscanBorderColor(bh);
                break;
            case 0x02:
                _vgaFunctions.SetAllPaletteRegisters(es, dx);
                break;
            case 0x03:
                _vgaFunctions.ToggleIntensity((bl & BiosConstants.Bit0Mask) != 0);
                break;
            case 0x07:
                if (bl > BiosConstants.MaxPaletteRegister) {
                    return (cx, dh, bx);
                }
                bh = _vgaFunctions.ReadPaletteRegister(bl);
                break;
            case 0x08:
                bh = _vgaFunctions.GetOverscanBorderColor();
                break;
            case 0x09:
                _vgaFunctions.GetAllPaletteRegisters(es, dx);
                break;
            case 0x10:
                _vgaFunctions.WriteToDac(bl, dh, (byte)(cx >> 8), (byte)cx);
                break;
            case 0x12:
                _vgaFunctions.WriteToDac(es, dx, bl, cx);
                if (_logger.IsEnabled(LogLevel.Debug)) {
                    _logger.LogDebug("{ClassName} INT 10 10 {MethodName} - set block of DAC color registers. {Amount} colors starting at register {StartRegister}, source address: {Segment:X4}:{Offset:X4}",
                        nameof(VgaBios), nameof(SetPaletteRegisters), bl, cx, es, dx);
                }
                break;
            case 0x13:
                if (_logger.IsEnabled(LogLevel.Debug)) {
                    _logger.LogDebug("{ClassName} INT 10 10 {MethodName} - Select color page, mode '{Mode}', value 0x{Value:X2} ",
                        nameof(VgaBios), nameof(SetPaletteRegisters), bl == 0 ? "set Mode Control register bit 7" : "set color select register", bh);
                }
                if (bl == 0) {
                    _vgaFunctions.SetP5P4Select((bh & BiosConstants.Bit0Mask) != 0);
                } else {
                    _vgaFunctions.SetColorSelectRegister(bh);
                }
                break;
            case 0x15:
                byte[] rgb = _vgaFunctions.ReadFromDac((byte)bx, 1);
                dh = rgb[0];
                cx = (ushort)((rgb[1] << 8) | rgb[2]);
                break;
            case 0x17:
                _vgaFunctions.ReadFromDac(es, dx, (byte)bx, cx);
                break;
            case 0x18:
                _vgaFunctions.WriteToPixelMask(bl);
                break;
            case 0x19:
                bl = _vgaFunctions.ReadPixelMask();
                break;
            case 0x1A:
                ushort colorPageState = _vgaFunctions.ReadColorPageState();
                bh = (byte)(colorPageState >> 8);
                bl = (byte)colorPageState;
                break;
            case 0x1B:
                _vgaFunctions.PerformGrayScaleSumming(bl, cx);
                break;
            default:
                throw new NotSupportedException($"0x{al:X2} is not a valid palette register subFunction");
        }
        return (cx, dh, (ushort)((bh << 8) | bl));
    }

    /// <inheritdoc />
    [InterruptService(0x0F, Reads = CpuValues.Ah, Writes = CpuValues.Ax | CpuValues.Bh, Memory = ServiceMemoryEffect.Read, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetVideoState() {
        (ushort ax, byte bh) = ReadVideoState();
        State.AX = ax;
        State.BH = bh;
    }

    /// <summary>
    ///     Returns the video mode in AL, the column count in AH and the active page in BH.
    /// </summary>
    public (ushort ax, byte bh) ReadVideoState() {
        byte bh = _biosDataArea.CurrentVideoPage;
        byte al = (byte)(_biosDataArea.VideoMode | _biosDataArea.VideoCtl & BiosConstants.VideoControlBitMask);
        byte ah = (byte)_biosDataArea.ScreenColumns;
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 0F {MethodName} - Page {Page}, mode {Mode}, columns {Columns}",
                nameof(VgaBios), nameof(GetVideoState), bh, al, ah);
        }
        return ((ushort)((ah << 8) | al), bh);
    }

    /// <inheritdoc />
    [InterruptService(0x0E, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteTextInTeletypeMode() {
        WriteTextInTeletypeMode(State.AL, State.BX);
    }

    /// <summary>
    ///     Writes character <paramref name="al"/> in teletype mode, with attribute BL.
    /// </summary>
    public void WriteTextInTeletypeMode(byte al, ushort bx) {
        CharacterPlusAttribute characterPlusAttribute = new((char)al, (byte)bx, false);
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(_biosDataArea.CurrentVideoPage);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 0E {MethodName} - {Character} Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteTextInTeletypeMode), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        _vgaFunctions.WriteTextInTeletypeMode(characterPlusAttribute);
    }

    /// <inheritdoc />
    [InterruptService(0x0B, Reads = CpuValues.Ah | CpuValues.Bx, Memory = ServiceMemoryEffect.Write, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetColorPaletteOrBackGroundColor() {
        SetColorPaletteOrBackGroundColor(State.BX);
    }

    /// <summary>
    ///     Sets the border color (BH=0) or the palette (any other BH) to BL.
    /// </summary>
    public void SetColorPaletteOrBackGroundColor(ushort bx) {
        byte bl = (byte)bx;
        if ((byte)(bx >> 8) == 0x00) {
            _vgaFunctions.SetBorderColor(bl);
            if (_logger.IsEnabled(LogLevel.Debug)) {
                _logger.LogDebug("{ClassName} INT 10 0B {MethodName} - Set border color {Color}",
                    nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), bl);
            }
        } else {
            // Most interrupt manuals say that BH can only be 0 and 1. De facto, any nonzero
            // value in BH should be handled as 1, because this was the behaviour of the IBM
            // BIOS:
            // https://github.com/gawlas/IBM-PC-BIOS/blob/master/IBM%20PC/PCBIOSV3.ASM#L3814

            _vgaFunctions.SetPalette(bl);
            if (_logger.IsEnabled(LogLevel.Debug)) {
                _logger.LogDebug("{ClassName} INT 10 0B {MethodName} - Set palette id {PaletteId}",
                    nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), bl);
            }
        }
    }

    /// <inheritdoc />
    [InterruptService(0x0A, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteCharacterAtCursor() {
        WriteCharacterAtCursor(State.AL, State.CX, State.BX);
    }

    /// <summary>
    ///     Writes character <paramref name="al"/>, CX times, at the cursor of page BH.
    /// </summary>
    public void WriteCharacterAtCursor(byte al, ushort cx, ushort bx) {
        CharacterPlusAttribute characterPlusAttribute = new((char)al, (byte)bx, false);
        byte currentVideoPage = (byte)(bx >> 8);
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(currentVideoPage);
        int count = cx;
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 0A {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }

        _vgaFunctions.WriteCharacterAtCursor(characterPlusAttribute, currentVideoPage, count);
    }

    /// <inheritdoc />
    [InterruptService(0x09, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteCharacterAndAttributeAtCursor() {
        WriteCharacterAndAttributeAtCursor(State.AL, State.CX, State.BX);
    }

    /// <summary>
    ///     Writes character <paramref name="al"/> with attribute BL, CX times, at the cursor of page BH.
    /// </summary>
    public void WriteCharacterAndAttributeAtCursor(byte al, ushort cx, ushort bx) {
        CharacterPlusAttribute characterPlusAttribute = new((char)al, (byte)bx, true);
        byte currentVideoPage = (byte)(bx >> 8);
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(currentVideoPage);
        int count = cx;
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 09 {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAndAttributeAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        _vgaFunctions.WriteCharacterAtCursor(characterPlusAttribute, currentVideoPage, count);
    }

    /// <inheritdoc />
    [InterruptService(0x08, Reads = CpuValues.Ah | CpuValues.Bx, Writes = CpuValues.Ax, Memory = ServiceMemoryEffect.Read, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ReadCharacterAndAttributeAtCursor() {
        State.AX = ReadCharacterAndAttributeAtCursor(State.BX);
    }

    /// <summary>
    ///     Returns the character in AL and attribute in AH at the cursor of page BH.
    /// </summary>
    public ushort ReadCharacterAndAttributeAtCursor(ushort bx) {
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition((byte)(bx >> 8));
        CharacterPlusAttribute characterPlusAttribute = _vgaFunctions.ReadChar(cursorPosition);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 08 {MethodName} - Character '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(ReadCharacterAndAttributeAtCursor), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        return (ushort)((characterPlusAttribute.Attribute << 8) | (byte)characterPlusAttribute.Character);
    }

    /// <inheritdoc />
    [InterruptService(0x07, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ScrollPageDown() {
        ScrollPageDown(State.AL, State.CX, State.DX, State.BX);
    }

    /// <summary>
    ///     Scrolls down the window at CX..DX by AL lines, filling with attribute BH.
    /// </summary>
    public void ScrollPageDown(byte al, ushort cx, ushort dx, ushort bx) {
        _vgaFunctions.VerifyScroll(-1, (byte)cx, (byte)(cx >> 8), (byte)dx, (byte)(dx >> 8), al, (byte)(bx >> 8));
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 07 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageDown), (byte)cx, (byte)(cx >> 8), (byte)dx, (byte)(dx >> 8), al, (byte)(bx >> 8));
        }
    }

    /// <inheritdoc />
    [InterruptService(0x06, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ScrollPageUp() {
        ScrollPageUp(State.AL, State.CX, State.DX, State.BX);
    }

    /// <summary>
    ///     Scrolls up the window at CX..DX by AL lines, filling with attribute BH.
    /// </summary>
    public void ScrollPageUp(byte al, ushort cx, ushort dx, ushort bx) {
        _vgaFunctions.VerifyScroll(1, (byte)cx, (byte)(cx >> 8), (byte)dx, (byte)(dx >> 8), al, (byte)(bx >> 8));
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 06 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageUp), (byte)cx, (byte)(cx >> 8), (byte)dx, (byte)(dx >> 8), al, (byte)(bx >> 8));
        }
    }

    /// <inheritdoc />
    [InterruptService(0x05, Reads = CpuValues.Ah | CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SelectActiveDisplayPage() {
        SelectActiveDisplayPage(State.AL);
    }

    /// <summary>
    ///     Selects the active display page.
    /// </summary>
    public void SelectActiveDisplayPage(byte al) {
        int address = _vgaFunctions.SetActivePage(al);

        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 05 {MethodName} - page {Page}, address {Address:X4}",
                nameof(VgaBios), nameof(SelectActiveDisplayPage), al, address);
        }
    }

    /// <inheritdoc />
    [InterruptService(0x03, Reads = CpuValues.Ah | CpuValues.Bx, Writes = CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.Read, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetCursorPosition() {
        (ushort cx, ushort dx) = GetCursorPosition(State.BX);
        State.CX = cx;
        State.DX = dx;
    }

    /// <summary>
    ///     Returns the cursor position of page BH in DX and its shape in CX.
    /// </summary>
    public (ushort cx, ushort dx) GetCursorPosition(ushort bx) {
        byte page = (byte)(bx >> 8);
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(page);
        ushort cx = _biosDataArea.CursorType;
        ushort dx = (ushort)(((byte)cursorPosition.Y << 8) | (byte)cursorPosition.X);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 03 {MethodName} - cursor position {X},{Y} on page {Page} type {Type}",
                nameof(VgaBios), nameof(GetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page, cx);
        }
        return (cx, dx);
    }

    /// <inheritdoc />
    [InterruptService(0x02, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetCursorPosition() {
        SetCursorPosition(State.DX, State.BX);
    }

    /// <summary>
    ///     Sets the cursor position on page BH to the row and column in DH and DL.
    /// </summary>
    public void SetCursorPosition(ushort dx, ushort bx) {
        CursorPosition cursorPosition = new((byte)dx, (byte)(dx >> 8), (byte)(bx >> 8));
        _vgaFunctions.SetCursorPosition(cursorPosition);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 02 {MethodName} - cursor position {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(SetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
    }

    /// <inheritdoc />
    [InterruptService(0x01, Reads = CpuValues.Ah | CpuValues.Cx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetCursorType() {
        SetCursorType(State.CX);
    }

    /// <summary>
    ///     Sets the text-mode cursor shape.
    /// </summary>
    public void SetCursorType(ushort cx) {
        _vgaFunctions.SetCursorShape(cx);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 01 {MethodName} - CX: {CX}",
                nameof(VgaBios), nameof(SetCursorType), cx);
        }
    }

    /// <inheritdoc />
    [InterruptService(0x0D, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Dx, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.Read, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ReadDot() {
        State.AL = ReadDot(State.CX, State.DX);
    }

    /// <summary>
    ///     Returns the pixel color at CX,DX.
    /// </summary>
    public byte ReadDot(ushort cx, ushort dx) {
        byte pixel = _vgaFunctions.ReadPixel(cx, dx);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 0D {MethodName} - pixel at {X},{Y} = 0x{Pixel:X2}",
                nameof(VgaBios), nameof(ReadDot), cx, dx, pixel);
        }
        return pixel;
    }

    /// <inheritdoc />
    [InterruptService(0x0C, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteDot() {
        WriteDot(State.AL, State.CX, State.DX);
    }

    /// <summary>
    ///     Writes a pixel of color <paramref name="al"/> at CX,DX.
    /// </summary>
    public void WriteDot(byte al, ushort cx, ushort dx) {
        _vgaFunctions.WritePixel(al, cx, dx);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 0C {MethodName} - write {Pixel} at {X},{Y}",
                nameof(VgaBios), nameof(WriteDot), al, cx, dx);
        }
    }

    /// <inheritdoc />
    [InterruptService(0x00, Reads = CpuValues.Ah | CpuValues.Al, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetVideoMode() {
        State.AL = SetVideoMode(State.AL);
    }

    /// <summary>
    ///     Sets the video mode to <paramref name="al"/> and returns the AL result.
    /// </summary>
    public byte SetVideoMode(byte al) {
        int modeId = al & BiosConstants.VideoModeMask;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_biosDataArea.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if ((al & BiosConstants.DontClearMemoryFlag) != 0) {
            flags |= ModeFlags.NoClearMem;
        }

        // Set AL
        if (modeId > 7) {
            al = BiosConstants.ModeAbove7Return;
        } else if (modeId == 6) {
            al = BiosConstants.Mode6Return;
        } else {
            al = BiosConstants.ModeBelow7Return;
        }
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 00 {MethodName} - mode {ModeId:X2}, {Flags}",
                nameof(VgaBios), nameof(SetVideoMode), modeId, flags);
        }
        _vgaFunctions.VgaSetMode(modeId, flags);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("VGA BIOS mode set complete");
        }
        return al;
    }

    /// <summary>
    ///     Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = State.AH;
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} running INT 10 operation 0x{Operation:X2}", nameof(VgaBios), operation);
        }

        const byte MshercGetVideoAdapterTypeAndMode = 0xEF;
        const byte UnsupportedButCommonCompatibilityCall = 0xFE;
        if (operation == MshercGetVideoAdapterTypeAndMode) {
            // operation installed by Microsoft MSHERC.COM/QBHERC.COM-TSRs (Hercules compatibility)
            // Ralph Browns interrupt list: https://mirror.math.princeton.edu/pub/oldlinux/Linux.old/docs/interrupts/int-html/rb-0549.htm
            // Microsoft KB Archive/69537: https://www.betaarchive.com/wiki/index.php/Microsoft_KB_Archive/69537
            // dosbox svn/staging just ignoring the interrupt
            // operation is used by Microsoft Quick/PDS Basic library
            // Gunboat ~1990 needs it
            if (_logger.IsEnabled(LogLevel.Warning)) {
                _logger.LogWarning("INT10H: Ignored VgaBios function number in AH register: {OperationNumber}", State.AH);
            }
            return;
        }

        if (operation == UnsupportedButCommonCompatibilityCall) {
            // Some DOS programs probe INT10 AH=FEh as a compatibility check.
            // dosbox-staging treats unknown INT10 calls as non-fatal, so ignore this call.
            if (_logger.IsEnabled(LogLevel.Warning)) {
                _logger.LogWarning("INT10H: Ignored compatibility VgaBios function number in AH register: {OperationNumber}", State.AH);
            }
            return;
        }

        if (!HasRunnable(operation) && _logger.IsEnabled(LogLevel.Error)) {
            _logger.LogError("INT10H: Unrecognized VgaBios function number in AH register: {OperationNumber}", State.AH);
        }
        Run(operation);
    }

    /// <inheritdoc />
    [InterruptService(0x04, Reads = CpuValues.Ah, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ReadLightPenPosition() {
        (ushort ax, ushort cx, ushort dx, ushort bx) = GetLightPenPosition();
        State.AX = ax;
        State.BX = bx;
        State.CX = cx;
        State.DX = dx;
    }

    /// <summary>
    ///     Reads the light pen position; always reports no light pen.
    /// </summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx) GetLightPenPosition() {
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 04 {MethodName} - Read Light Pen Position, returning 0",
                nameof(VgaBios), nameof(ReadLightPenPosition));
        }
        return (0, 0, 0, 0);
    }

    private void InitializeBiosArea() {
        // init detected hardware BIOS Area
        // set 80x25 color (not clear from RBIL but usual)
        _biosDataArea.EquipmentListFlags = (ushort)(_biosDataArea.EquipmentListFlags & ~BiosConstants.EquipmentListFlagsMask | BiosConstants.VideoControl80x25Color);

        // Set the basic modeset options
        _biosDataArea.ModesetCtl = BiosConstants.DefaultModeSetControl;
        _biosDataArea.DisplayCombinationCode = BiosConstants.DefaultDisplayCombinationCode;
    }

    private byte VideoScreenOnOff() {
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 36 {MethodName} - Ignored",
                nameof(VgaBios), nameof(VideoScreenOnOff));
        }
        return BiosConstants.SubfunctionSuccess;
    }

    private byte DisplaySwitch() {
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 35 {MethodName} - Ignored",
                nameof(VgaBios), nameof(DisplaySwitch));
        }
        return BiosConstants.SubfunctionSuccess;
    }

    private byte CursorEmulation(byte al) {
        bool enabled = (al & BiosConstants.UpdateCursorPositionFlag) == 0;
        _vgaFunctions.CursorEmulation(enabled);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 34 {MethodName} - {Result}",
                nameof(VgaBios), nameof(CursorEmulation), enabled ? "Enabled" : "Disabled");
        }
        return BiosConstants.SubfunctionSuccess;
    }

    private byte SummingToGrayScales(byte al) {
        bool enabled = (al & BiosConstants.UpdateCursorPositionFlag) == 0;
        _vgaFunctions.SummingToGrayScales(enabled);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 33 {MethodName} - {Result}",
                nameof(VgaBios), nameof(SummingToGrayScales), enabled ? "Enabled" : "Disabled");
        }
        return BiosConstants.SubfunctionSuccess;
    }

    private byte VideoEnableDisable(byte al) {
        _vgaFunctions.EnableVideoAddressing((al & BiosConstants.UpdateCursorPositionFlag) == 0);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 32 {MethodName} - {Result}",
                nameof(VgaBios), nameof(VideoEnableDisable), (al & BiosConstants.UpdateCursorPositionFlag) == 0 ? "Enabled" : "Disabled");
        }
        return BiosConstants.SubfunctionSuccess;
    }

    private byte DefaultPaletteLoading(byte al) {
        _vgaFunctions.DefaultPaletteLoading((al & BiosConstants.UpdateCursorPositionFlag) != 0);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 31 {MethodName} - 0x{Al:X2}",
                nameof(VgaBios), nameof(DefaultPaletteLoading), al);
        }
        return BiosConstants.SubfunctionSuccess;
    }

    private byte SelectScanLines(byte al) {
        int lines = al switch {
            0x00 => BiosConstants.ScanLines200,
            0x01 => BiosConstants.ScanLines350,
            0x02 => BiosConstants.ScanLines400,
            _ => throw new NotSupportedException($"AL=0x{al:X2} is not a valid subFunction for INT 10 12 30")
        };
        _vgaFunctions.SelectScanLines(lines);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 30 {MethodName} - {Lines} lines",
                nameof(VgaBios), nameof(SelectScanLines), lines);
        }
        return BiosConstants.SubfunctionSuccess;
    }

    private (ushort cx, ushort bx) EgaVgaInformation() {
        byte bh = (byte)(_vgaFunctions.GetColorMode() ? BiosConstants.ColorModeMemory : 0x00);
        ushort cx = _vgaFunctions.GetFeatureSwitches();
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 12 10 {MethodName} - ColorMode 0x{ColorMode:X2}, Memory: 0x{Memory:X2}, FeatureSwitches: 0x{FeatureSwitches:X2}",
                nameof(VgaBios), nameof(EgaVgaInformation), bh, BiosConstants.Memory256KB, cx);
        }
        return (cx, (ushort)((bh << 8) | BiosConstants.Memory256KB));
    }

    private void SetBlockSpecifier(byte fontBlock) {
        _vgaFunctions.SetFontBlockSpecifier(fontBlock);
    }

    [InterruptService(0x1B, Name = "GetFunctionalityInfo", Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Es | CpuValues.Di, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    private void FunctionalityInfoService() {
        State.AL = GetFunctionalityInfo(State.BX, State.ES, State.DI).al;
    }

    /// <summary>
    /// VESA VBE 1.2 function dispatcher (INT 10h AH=4Fh).
    /// Dispatches to specific VBE functions based on AL subfunction.
    /// </summary>
    [InterruptService(0x4F, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Es | CpuValues.Di, Writes = CpuValues.Ax, Memory = ServiceMemoryEffect.ReadWrite, Io = true, ExecutionPath = ServiceExecutionPath.Direct)]
    public void VesaFunctions() {
        State.AX = VesaFunctions(State.AL, State.CX, State.BX, State.DI, State.ES);
    }

    /// <summary>
    ///     VESA VBE 1.2 function dispatcher: runs the VBE subfunction selected by
    ///     <paramref name="al"/> and returns the AX status word.
    /// </summary>
    public ushort VesaFunctions(byte al, ushort cx, ushort bx, ushort di, ushort es) {
        switch ((VbeFunction)al) {
            case VbeFunction.GetControllerInfo:
                return VbeGetControllerInfo(es, di);
            case VbeFunction.GetModeInfo:
                return VbeGetModeInfo(cx, es, di);
            case VbeFunction.SetMode:
                return VbeSetMode(bx);
            default:
                if (_logger.IsEnabled(LogLevel.Warning)) {
                    _logger.LogWarning("{ClassName} INT 10 4F{Subfunction:X2} - Unsupported VBE function",
                        nameof(VgaBios), al);
                }
                return (ushort)VbeStatus.Failed;
        }
    }

    /// <inheritdoc cref="IVesaBiosExtension.VbeGetControllerInfo"/>
    public void VbeGetControllerInfo() {
        State.AX = VbeGetControllerInfo(State.ES, State.DI);
    }

    /// <summary>
    ///     Fills the VBE info block at <paramref name="segment"/>:<paramref name="offset"/>
    ///     and returns the AX status word.
    /// </summary>
    public ushort VbeGetControllerInfo(ushort segment, ushort offset) {
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);

        VbeInfoBlock vbeInfo = new VbeInfoBlock(Memory, address);
        ZeroMemory(address, VbeConstants.ControllerInfoBlockSize);

        // Fill VBE Info Block (VBE 1.2)
        vbeInfo.Signature = "VESA";
        vbeInfo.Version = VbeConstants.Version12;

        // Capabilities: DAC is switchable, controller is VGA compatible
        vbeInfo.Capabilities = VbeConstants.DacSwitchableCapability;

        uint oemStringAddress = MemoryUtils.ToPhysicalAddress(VbeConstants.VbeDataSegment, VbeConstants.OemStringOffset);
        (ushort oemStringSegment, ushort oemStringOffset) = ToFarPointer(oemStringAddress);
        vbeInfo.OemStringOffset = oemStringOffset;
        vbeInfo.OemStringSegment = oemStringSegment;

        uint modeListAddress = MemoryUtils.ToPhysicalAddress(VbeConstants.VbeDataSegment, VbeConstants.ModeListOffset);
        (ushort modeListSegment, ushort modeListOffset) = ToFarPointer(modeListAddress);
        vbeInfo.VideoModeListOffset = modeListOffset;
        vbeInfo.VideoModeListSegment = modeListSegment;

        // Total Memory in 64KB blocks (256KB = 4 blocks)
        vbeInfo.TotalMemory = VbeConstants.TotalMemory256KB;

        Memory.SetZeroTerminatedString(oemStringAddress, VbeConstants.OemString);

        Memory.WriteUInt16Segmented(modeListSegment, modeListOffset, VbeConstants.VesaMode800x600x16);
        Memory.WriteUInt16Segmented(modeListSegment, (ushort)(modeListOffset + 2), VbeConstants.ModeListTerminator);

        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 4F00 VbeGetControllerInfo - Returning VBE 1.2 info at {Segment:X4}:{Offset:X4}, OEM {OemSegment:X4}:{OemOffset:X4}, modes {ModeSegment:X4}:{ModeOffset:X4}",
                nameof(VgaBios), segment, offset, oemStringSegment, oemStringOffset, modeListSegment, modeListOffset);
        }

        return (ushort)VbeStatus.Success;
    }

    /// <inheritdoc cref="IVesaBiosExtension.VbeGetModeInfo"/>
    public void VbeGetModeInfo() {
        State.AX = VbeGetModeInfo(State.CX, State.ES, State.DI);
    }

    /// <summary>
    ///     Fills the VBE mode info block at <paramref name="segment"/>:<paramref name="offset"/>
    ///     for the requested mode and returns the AX status word.
    /// </summary>
    public ushort VbeGetModeInfo(ushort requestedModeNumber, ushort segment, ushort offset) {
        ushort modeNumber = (ushort)(requestedModeNumber & (ushort)VbeModeFlags.ModeNumberMask);
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);

        // Get mode parameters based on VESA base mode number (without high flag bits)
        (ushort width, ushort height, byte bpp, bool supported) = GetVesaModeParams(modeNumber);

        if (!supported) {
            if (_logger.IsEnabled(LogLevel.Warning)) {
                _logger.LogWarning("{ClassName} INT 10 4F01 VbeGetModeInfo - Unsupported mode 0x{Mode:X4}",
                    nameof(VgaBios), requestedModeNumber);
            }
            return (ushort)VbeStatus.Failed;
        }

        VbeModeInfoBlock modeInfo = new VbeModeInfoBlock(Memory, address);
        ZeroMemory(address, VbeConstants.ModeInfoBlockSize);

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

        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 4F01 VbeGetModeInfo - Mode 0x{Mode:X4}: {Width}x{Height}x{Bpp}",
                nameof(VgaBios), modeNumber, width, height, bpp);
        }

        // Return success
        return (ushort)VbeStatus.Success;
    }

    /// <inheritdoc cref="IVesaBiosExtension.VbeSetMode"/>
    public void VbeSetMode() {
        State.AX = VbeSetMode(State.BX);
    }

    /// <summary>
    ///     Sets the VESA mode <paramref name="modeNumber"/> and returns the AX status word.
    /// </summary>
    public ushort VbeSetMode(ushort modeNumber) {
        // VBE 1.0/1.2 does not support LFB; bit 14 is ignored (banked mode is always used)
        bool dontClearDisplay = (modeNumber & (ushort)VbeModeFlags.DontClearMemory) != 0;
        ushort mode = (ushort)(modeNumber & (ushort)VbeModeFlags.ModeNumberMask);

        // Map VESA mode to internal mode
        int? internalMode = MapVesaModeToInternal(mode);

        if (!internalMode.HasValue) {
            if (_logger.IsEnabled(LogLevel.Warning)) {
                _logger.LogWarning("{ClassName} INT 10 4F02 VbeSetMode - Unsupported mode 0x{Mode:X4}",
                    nameof(VgaBios), mode);
            }
            return (ushort)VbeStatus.Failed;
        }

        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_biosDataArea.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if (dontClearDisplay) {
            flags |= ModeFlags.NoClearMem;
        }

        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("{ClassName} INT 10 4F02 VbeSetMode - Setting VESA mode 0x{VesaMode:X4} (internal mode 0x{InternalMode:X2})",
                nameof(VgaBios), mode, internalMode.Value);
        }

        _vgaFunctions.VgaSetMode(internalMode.Value, flags);

        return (ushort)VbeStatus.Success;
    }

    /// <summary>
    /// Gets the parameters for a VESA mode number.
    /// Returns mode information for VESA modes currently supported by this VGA implementation.
    /// This is used by VbeGetModeInfo (VBE 01h) to return mode characteristics.
    /// supported=true means the mode can be queried and set by the emulator.
    /// </summary>
    private static (ushort width, ushort height, byte bpp, bool supported) GetVesaModeParams(ushort mode) {
        return mode switch {
            VbeConstants.VesaMode800x600x16 => (800, 600, 4, true),
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

    private void ZeroMemory(uint baseAddress, int length) {
        byte[] empty = new byte[length];
        Memory.WriteRam(empty, baseAddress);
    }

    private static (ushort Segment, ushort Offset) ToFarPointer(uint physicalAddress) {
        ushort segment = MemoryUtils.ToSegment(physicalAddress);
        ushort offset = (ushort)(physicalAddress & 0x0F);

        return (segment, offset);
    }

    /// <inheritdoc />
    public VideoFunctionalityInfo GetFunctionalityInfo() {
        (VideoFunctionalityInfo info, byte al) = GetFunctionalityInfo(State.BX, State.ES, State.DI);
        State.AL = al;
        return info;
    }

    /// <summary>
    ///     Writes the video functionality information block at
    ///     <paramref name="segment"/>:<paramref name="offset"/> for function
    ///     <paramref name="function"/> and returns it with the AL status.
    /// </summary>
    public (VideoFunctionalityInfo info, byte al) GetFunctionalityInfo(ushort function, ushort segment, ushort offset) {
        byte al;
        switch (function) {
            case 0x0:
                al = BiosConstants.FunctionalityInfoSupported;
                break;
            default:
                if (_logger.IsEnabled(LogLevel.Warning)) {
                    _logger.LogWarning("{ClassName} INT 10 1B: {MethodName} - Unsupported subFunction 0x{SubFunction:X2}",
                        nameof(VgaBios), nameof(GetFunctionalityInfo), function);
                }
                al = 0;
                break;
        }

        VgaMode currentMode = _vgaFunctions.GetCurrentMode();
        ushort cursorType = _biosDataArea.CursorType;
        byte characterMapRegister = _vgaFunctions.GetCharacterMapSelectRegister();
        (byte primaryCharacterTable, byte secondaryCharacterTable) = DecodeCharacterMapSelections(characterMapRegister);

        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
        var info = new VideoFunctionalityInfo(Memory, address) {
            SftAddress = new SegmentedAddress(MemoryMap.StaticFunctionalityTableSegment, 0),
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

        if (_logger.IsEnabled(LogLevel.Warning)) {
            _logger.LogWarning("{ClassName} INT 10 1B: {MethodName} - experimental! {@Summary}",
                nameof(VgaBios), nameof(GetFunctionalityInfo), info.CreateFunctionalityInfoLogSnapshot());
        }
        return (info, al);
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
