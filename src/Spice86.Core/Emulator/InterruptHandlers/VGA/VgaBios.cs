namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
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
    public VgaBios(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack,  State state, IVgaFunctionality vgaFunctions, BiosDataArea biosDataArea, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _vgaFunctions = vgaFunctions;
        _logger = loggerService;
        if(_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Initializing VGA BIOS");
        }
        FillDispatchTable();

        InitializeBiosArea();
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
    public VideoFunctionalityInfo GetFunctionalityInfo() {
        throw new NotImplementedException();
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
    }

    // public VideoFunctionalityInfo GetFunctionalityInfo() {
    //     ushort segment = _state.ES;
    //     ushort offset = _state.DI;
    //
    //     uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
    //     var info = new VideoFunctionalityInfo(_memory, address) {
    //         SftAddress = MemoryMap.StaticFunctionalityTableSegment << 16,
    //         VideoMode = _bios.VideoMode,
    //         ScreenColumns = _bios.ScreenColumns,
    //         VideoBufferLength = MemoryMap.VideoBiosSegment - MemoryMap.GraphicVideoMemorySegment, // TODO: real value
    //         VideoBufferAddress = MemoryMap.GraphicVideoMemorySegment, // TODO: real value
    //         CursorEndLine = 0, // TODO: figure out what this is
    //         CursorStartLine = 0, // TODO: figure out what this is
    //         ActiveDisplayPage = (byte)CurrentMode.ActiveDisplayPage,
    //         CrtControllerBaseAddress = _bios.CrtControllerBaseAddress,
    //         CurrentRegister3X8Value = 0, // Unused in VGA
    //         CurrentRegister3X9Value = 0, // Unused in VGA
    //         ScreenRows = _bios.ScreenRows,
    //         CharacterMatrixHeight = (ushort)CurrentMode.FontHeight,
    //         ActiveDisplayCombinationCode = _bios.DisplayCombinationCode,
    //         AlternateDisplayCombinationCode = 0x00, // No secondary display
    //         NumberOfColorsSupported = (ushort)(1 << CurrentMode.BitsPerPixel),
    //         NumberOfPages = 4,
    //         NumberOfActiveScanLines = 0, // TODO: figure out what this is
    //         TextCharacterTableUsed = 0, // TODO: figure out what this is
    //         TextCharacterTableUsed2 = 0, // TODO: figure out what this is
    //         OtherStateInformation = 0b00000001,
    //         VideoRamAvailable = 3, // 0=64K, 1=128K, 2=192K, 3=256K
    //         SaveAreaStatus = 0b00000000
    //     };
    //     for (int i = 0; i < 8; i++) {
    //         // TODO: fix
    //         // info.SetCursorPosition(i, (byte)TextConsole.CursorPosition.X, (byte)TextConsole.CursorPosition.Y);
    //     }
    //
    //     // Indicate success.
    //     _state.AL = 0x1B;
    //     if (_logger.IsEnabled(LogEventLevel.Debug)) {
    //         _logger.Debug("INT 10: GetFunctionalityInfo {0}", info);
    //     }
    //     return info;
    // }
    //
    // /// <summary>
    // /// Writes values to the static functionality table in emulated memory.
    // /// </summary>
    // private void InitializeStaticFunctionalityTable() {
    //     _memory.UInt32[MemoryMap.StaticFunctionalityTableSegment, 0] = 0x000FFFFF; // supports all video modes
    //     _memory.UInt8[MemoryMap.StaticFunctionalityTableSegment, 0x07] = 0x07; // supports all scanLines
    // }
}