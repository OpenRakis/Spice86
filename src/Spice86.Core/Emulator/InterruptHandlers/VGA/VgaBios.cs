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
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
///     A VGA BIOS implementation.
/// </summary>
public class VgaBios : InterruptHandler, IVideoInt10Handler {
    private readonly BiosDataArea _biosDataArea;
    private readonly ILoggerService _logger;
    private readonly IVgaFunctionality _vgaFunctions;

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
        bool includeAttributes = (State.AL & 0x02) != 0;
        bool updateCursorPosition = (State.AL & 0x01) != 0;
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
                    State.AL = 0x1A; // Function supported
                    State.BL = _biosDataArea.DisplayCombinationCode; // Primary display
                    State.BH = 0x00; // No secondary display
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        _logger.Debug("{ClassName} INT 10 1A {MethodName} - Get: DCC 0x{Dcc:X2}",
                            nameof(VgaBios), nameof(GetSetDisplayCombinationCode), State.BL);
                    }
                    break;
                }
            case 0x01: {
                    State.AL = 0x1A; // Function supported
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
        switch (State.BL) {
            case 0x10:
                EgaVgaInformation();
                break;
            case 0x30:
                SelectScanLines();
                break;
            case 0x31:
                DefaultPaletteLoading();
                break;
            case 0x32:
                VideoEnableDisable();
                break;
            case 0x33:
                SummingToGrayScales();
                break;
            case 0x34:
                CursorEmulation();
                break;
            case 0x35:
                DisplaySwitch();
                break;
            case 0x36:
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
                _vgaFunctions.ToggleIntensity((State.BL & 1) != 0);
                break;
            case 0x07:
                if (State.BL > 0xF) {
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
                    _vgaFunctions.SetP5P4Select((State.BH & 1) != 0);
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
        State.AL = (byte)(_biosDataArea.VideoMode | _biosDataArea.VideoCtl & 0x80);
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
        switch (State.BH) {
            case 0x00:
                _vgaFunctions.SetBorderColor(State.BL);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 0B {MethodName} - Set border color {Color}",
                        nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), State.BL);
                }
                break;
            case 0x01:
                _vgaFunctions.SetPalette(State.BL);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 0B {MethodName} - Set palette id {PaletteId}",
                        nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), State.BL);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(State.BH), State.BH, $"INT 10: {nameof(SetColorPaletteOrBackGroundColor)} Invalid subFunction 0x{State.BH:X2}");
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
        int modeId = State.AL & 0x7F;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_biosDataArea.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if ((State.AL & 0x80) != 0) {
            flags |= ModeFlags.NoClearMem;
        }

        // Set AL
        if (modeId > 7) {
            State.AL = 0x20;
        } else if (modeId == 6) {
            State.AL = 0x3F;
        } else {
            State.AL = 0x30;
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
        _biosDataArea.EquipmentListFlags = (ushort)(_biosDataArea.EquipmentListFlags & ~0x30 | 0x20);

        // Set the basic modeset options
        _biosDataArea.ModesetCtl = 0x51;
        _biosDataArea.DisplayCombinationCode = 0x08;
    }

    private void VideoScreenOnOff() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 36 {MethodName} - Ignored",
                nameof(VgaBios), nameof(VideoScreenOnOff));
        }
        State.AL = 0x12;
    }

    private void DisplaySwitch() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 35 {MethodName} - Ignored",
                nameof(VgaBios), nameof(DisplaySwitch));
        }
        State.AL = 0x12;
    }

    private void CursorEmulation() {
        bool enabled = (State.AL & 0x01) == 0;
        _vgaFunctions.CursorEmulation(enabled);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 34 {MethodName} - {Result}",
                nameof(VgaBios), nameof(CursorEmulation), enabled ? "Enabled" : "Disabled");
        }
        State.AL = 0x12;
    }

    private void SummingToGrayScales() {
        bool enabled = (State.AL & 0x01) == 0;
        _vgaFunctions.SummingToGrayScales(enabled);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 33 {MethodName} - {Result}",
                nameof(VgaBios), nameof(SummingToGrayScales), enabled ? "Enabled" : "Disabled");
        }
        State.AL = 0x12;
    }

    private void VideoEnableDisable() {
        _vgaFunctions.EnableVideoAddressing((State.AL & 1) == 0);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 32 {MethodName} - {Result}",
                nameof(VgaBios), nameof(VideoEnableDisable), (State.AL & 0x01) == 0 ? "Enabled" : "Disabled");
        }
        State.AL = 0x12;
    }

    private void DefaultPaletteLoading() {
        _vgaFunctions.DefaultPaletteLoading((State.AL & 1) != 0);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 31 {MethodName} - 0x{Al:X2}",
                nameof(VgaBios), nameof(DefaultPaletteLoading), State.AL);
        }
        State.AL = 0x12;
    }

    private void SelectScanLines() {
        int lines = State.AL switch {
            0x00 => 200,
            0x01 => 350,
            0x02 => 400,
            _ => throw new NotSupportedException($"AL=0x{State.AL:X2} is not a valid subFunction for INT 10 12 30")
        };
        _vgaFunctions.SelectScanLines(lines);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 30 {MethodName} - {Lines} lines",
                nameof(VgaBios), nameof(SelectScanLines), lines);
        }
        State.AL = 0x12;
    }

    private void EgaVgaInformation() {
        State.BH = (byte)(_vgaFunctions.GetColorMode() ? 0x01 : 0x00);
        State.BL = 0x03;
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
        switch (subfunction) {
            case 0x00:
                VbeGetControllerInfo();
                break;
            case 0x01:
                VbeGetModeInfo();
                break;
            case 0x02:
                VbeSetMode();
                break;
            default:
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning("{ClassName} INT 10 4F{Subfunction:X2} - Unsupported VBE function",
                        nameof(VgaBios), subfunction);
                }
                // Return failure: AH=01h (function call failed), AL=4Fh (function is supported)
                State.AX = 0x014F;
                break;
        }
    }

    /// <summary>
    /// VBE Function 00h - Return VBE Controller Information.
    /// ES:DI = Pointer to VbeInfoBlock structure (256 bytes minimum, but caller should provide ~512 bytes
    /// to accommodate the OEM string and mode list stored beyond the structure).
    /// Returns: AX = VBE Return Status (004Fh = success).
    /// </summary>
    private void VbeGetControllerInfo() {
        ushort segment = State.ES;
        ushort offset = State.DI;
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);

        // Check if buffer has "VBE2" signature (VBE 2.0+ request)
        // For VBE 1.0, we just fill the basic info

        // Write VBE Info Block (VBE 1.0 structure - 256 bytes minimum)
        // Signature: "VESA"
        Memory.UInt8[address + 0] = (byte)'V';
        Memory.UInt8[address + 1] = (byte)'E';
        Memory.UInt8[address + 2] = (byte)'S';
        Memory.UInt8[address + 3] = (byte)'A';

        // VBE Version: 1.0 (0x0100)
        Memory.UInt16[address + 4] = 0x0100;

        // OEM String pointer (segment:offset) - point to a location in VGA ROM
        // We'll use a dummy OEM string at ES:DI+256
        Memory.UInt16[address + 6] = (ushort)(offset + 256);  // Offset
        Memory.UInt16[address + 8] = segment;                  // Segment

        // Capabilities (4 bytes) - DAC is switchable, controller is VGA compatible
        Memory.UInt32[address + 10] = 0x00000001;

        // Video Mode List pointer (segment:offset) - point after OEM string
        Memory.UInt16[address + 14] = (ushort)(offset + 280);  // Offset
        Memory.UInt16[address + 16] = segment;                  // Segment

        // Total Memory in 64KB blocks (1MB = 16 blocks)
        const ushort videoMemoryIn64KbBlocks = 16;
        Memory.UInt16[address + 18] = videoMemoryIn64KbBlocks;

        // Write OEM String at offset+256
        string oemString = "Spice86 VBE";
        for (int i = 0; i < oemString.Length; i++) {
            Memory.UInt8[address + 256 + i] = (byte)oemString[i];
        }
        Memory.UInt8[address + 256 + oemString.Length] = 0; // Null terminator

        // Write Video Mode List at offset+280
        // Only list modes that have corresponding internal VGA modes
        // Per VBE 1.2 spec, modes without hardware support should not be listed
        ushort[] vesaModes = {
            0x102, // 800x600x16 (4-bit planar) -> internal mode 0x6A
            0xFFFF // End of list
        };

        for (int i = 0; i < vesaModes.Length; i++) {
            Memory.UInt16[address + 280 + (i * 2)] = vesaModes[i];
        }

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 4F00 VbeGetControllerInfo - Returning VBE 1.0 info at {Segment:X4}:{Offset:X4}",
                nameof(VgaBios), segment, offset);
        }

        // Return success: AH=00h (function successful), AL=4Fh (function is supported)
        State.AX = 0x004F;
    }

    /// <summary>
    /// VBE Function 01h - Return VBE Mode Information.
    /// CX = Mode number.
    /// ES:DI = Pointer to ModeInfoBlock structure (256 bytes).
    /// Returns: AX = VBE Return Status (004Fh = success).
    /// </summary>
    private void VbeGetModeInfo() {
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
            // Return failure
            State.AX = 0x014F;
            return;
        }

        // Clear the mode info block (256 bytes)
        for (int i = 0; i < 256; i++) {
            Memory.UInt8[address + i] = 0;
        }

        // Mode Attributes (offset 0, 2 bytes)
        // Bit 0: Mode supported by hardware
        // Bit 1: Extended information available
        // Bit 2: TTY output functions supported
        // Bit 3: Color mode
        // Bit 4: Graphics mode (not text)
        ushort modeAttributes = 0x001B; // Supported, extended info, TTY, color, graphics
        Memory.UInt16[address + 0] = modeAttributes;

        // Window A attributes (offset 2)
        Memory.UInt8[address + 2] = 0x07; // Readable, writable, supported

        // Window B attributes (offset 3)
        Memory.UInt8[address + 3] = 0x00; // Not supported

        // Window granularity in KB (offset 4)
        Memory.UInt16[address + 4] = 64;

        // Window size in KB (offset 6)
        Memory.UInt16[address + 6] = 64;

        // Window A start segment (offset 8)
        Memory.UInt16[address + 8] = 0xA000;

        // Window B start segment (offset 10)
        Memory.UInt16[address + 10] = 0x0000;

        // Window function pointer (offset 12, 4 bytes) - not used in VBE 1.0
        Memory.UInt32[address + 12] = 0;

        // Bytes per scan line (offset 16)
        // Per VBE 1.2 spec, this is the number of bytes between consecutive scanlines
        // For planar modes (4-bit), this is bytes per plane (width/8 since 1 bit per pixel per plane)
        ushort bytesPerLine;
        if (bpp == 4) {
            // 4-bit planar: 4 planes, each with 1 bit per pixel
            bytesPerLine = (ushort)(width / 8);
        } else if (bpp == 1) {
            bytesPerLine = (ushort)(width / 8);
        } else if (bpp == 15 || bpp == 16) {
            // 15-bit and 16-bit modes use 2 bytes per pixel
            bytesPerLine = (ushort)(width * 2);
        } else if (bpp == 24) {
            bytesPerLine = (ushort)(width * 3);
        } else if (bpp == 32) {
            bytesPerLine = (ushort)(width * 4);
        } else {
            // 8-bit packed pixel (bpp == 8)
            bytesPerLine = width;
        }
        Memory.UInt16[address + 16] = bytesPerLine;

        // X resolution (offset 18)
        Memory.UInt16[address + 18] = width;

        // Y resolution (offset 20)
        Memory.UInt16[address + 20] = height;

        // X char size (offset 22)
        Memory.UInt8[address + 22] = 8;

        // Y char size (offset 23)
        Memory.UInt8[address + 23] = 16;

        // Number of planes (offset 24)
        Memory.UInt8[address + 24] = (byte)(bpp == 4 ? 4 : 1);

        // Bits per pixel (offset 25)
        Memory.UInt8[address + 25] = bpp;

        // Number of banks (offset 26)
        Memory.UInt8[address + 26] = 1;

        // Memory model (offset 27)
        // 0 = Text, 3 = EGA, 4 = Packed pixel, 5 = Non-chain 4 256 color, 6 = Direct color
        byte memoryModel = bpp switch {
            4 => 3,  // EGA/VGA planar
            8 => 4,  // Packed pixel
            15 => 6, // Direct color
            16 => 6, // Direct color
            24 => 6, // Direct color
            32 => 6, // Direct color
            _ => 4
        };
        Memory.UInt8[address + 27] = memoryModel;

        // Bank size in KB (offset 28)
        Memory.UInt8[address + 28] = 64;

        // Number of image pages (offset 29)
        Memory.UInt8[address + 29] = 0;

        // Reserved (offset 30)
        Memory.UInt8[address + 30] = 1;

        // Direct color fields (for 15/16/24/32 bpp modes)
        if (bpp >= 15) {
            if (bpp == 15 || bpp == 16) {
                // 15-bit: 5:5:5 format (R=5 bits at position 10, G=5 bits at position 5, B=5 bits at position 0)
                // 16-bit: 5:6:5 format (R=5 bits at position 11, G=6 bits at position 5, B=5 bits at position 0)
                Memory.UInt8[address + 31] = 5;  // Red mask size
                Memory.UInt8[address + 32] = (byte)(bpp == 16 ? 11 : 10); // Red field position
                Memory.UInt8[address + 33] = (byte)(bpp == 16 ? 6 : 5);   // Green mask size
                Memory.UInt8[address + 34] = 5;  // Green field position
                Memory.UInt8[address + 35] = 5;  // Blue mask size
                Memory.UInt8[address + 36] = 0;  // Blue field position
            } else if (bpp == 24 || bpp == 32) {
                Memory.UInt8[address + 31] = 8;  // Red mask size
                Memory.UInt8[address + 32] = 16; // Red field position
                Memory.UInt8[address + 33] = 8;  // Green mask size
                Memory.UInt8[address + 34] = 8;  // Green field position
                Memory.UInt8[address + 35] = 8;  // Blue mask size
                Memory.UInt8[address + 36] = 0;  // Blue field position
            }
        }

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 4F01 VbeGetModeInfo - Mode 0x{Mode:X4}: {Width}x{Height}x{Bpp}",
                nameof(VgaBios), modeNumber, width, height, bpp);
        }

        // Return success
        State.AX = 0x004F;
    }

    /// <summary>
    /// VBE Function 02h - Set VBE Mode.
    /// BX = Mode number (bit 14 = use linear frame buffer if set, bit 15 = don't clear display).
    /// Note: VBE 1.0/1.2 does not support linear frame buffer (LFB) - that was introduced in VBE 2.0.
    /// If bit 14 is set, this implementation ignores it and uses banked mode; programs expecting
    /// LFB access will not work correctly but the mode set will succeed.
    /// Returns: AX = VBE Return Status (004Fh = success).
    /// </summary>
    private void VbeSetMode() {
        ushort modeNumber = State.BX;
        // VBE 1.0/1.2 does not support LFB; bit 14 is ignored (banked mode is always used)
        bool dontClearDisplay = (modeNumber & 0x8000) != 0;
        ushort mode = (ushort)(modeNumber & 0x01FF);

        // Map VESA mode to internal mode
        int? internalMode = MapVesaModeToInternal(mode);

        if (!internalMode.HasValue) {
            if (_logger.IsEnabled(LogEventLevel.Warning)) {
                _logger.Warning("{ClassName} INT 10 4F02 VbeSetMode - Unsupported mode 0x{Mode:X4}",
                    nameof(VgaBios), mode);
            }
            // Return failure
            State.AX = 0x014F;
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

        // Return success
        State.AX = 0x004F;
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
            0x102 => 0x6A,  // 800x600x16 (planar) -> internal mode 0x6A
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
                State.AL = 0x1B;
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
            AlternateDisplayCombinationCode = 0x00,
            NumberOfColorsSupported = CalculateColorCount(currentMode),
            NumberOfPages = CalculatePageCount(currentMode),
            NumberOfActiveScanLines = CalculateScanLineCode(currentMode),
            TextCharacterTableUsed = primaryCharacterTable,
            TextCharacterTableUsed2 = secondaryCharacterTable,
            OtherStateInformation = GetOtherStateInformation(currentMode),
            VideoRamAvailable = 3,
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
        Memory.UInt32[MemoryMap.StaticFunctionalityTableSegment, 0] = 0x000FFFFF; // supports all video modes
        Memory.UInt8[MemoryMap.StaticFunctionalityTableSegment, 0x07] = 0x07; // supports all scanLines
    }

    private static byte ExtractCursorStartLine(ushort cursorType) {
        return (byte)((cursorType >> 8) & 0x3F);
    }

    private static byte ExtractCursorEndLine(ushort cursorType) {
        return (byte)(cursorType & 0x1F);
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