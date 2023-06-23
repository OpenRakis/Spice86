namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
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
    /// <param name="machine">The machine hosting the bios.</param>
    /// <param name="vgaFunctions">Provides vga functionality to use by the interrupt handler</param>
    /// <param name="biosDataArea">Contains the global bios data values</param>
    /// <param name="loggerService">A logger</param>
    public VgaBios(Machine machine, IVgaFunctionality vgaFunctions, BiosDataArea biosDataArea, ILoggerService loggerService) : base(machine, loggerService) {
        _biosDataArea = biosDataArea;
        _vgaFunctions = vgaFunctions;
        _logger = loggerService;
        _logger.Debug("Initializing VGA BIOS");
        FillDispatchTable();

        InitializeBiosArea();
    }

    /// <summary>
    ///     The interrupt vector this class handles.
    /// </summary>
    public override byte Index => 0x10;

    /// <inheritdoc />
    public void WriteString() {
        CursorPosition cursorPosition = new(_state.DL, _state.DH, _state.BH);
        ushort length = _state.CX;
        ushort segment = _state.ES;
        ushort offset = _state.BP;
        byte attribute = _state.BL;
        bool includeAttributes = (_state.AL & 0x02) != 0;
        bool updateCursorPosition = (_state.AL & 0x01) != 0;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
            string str = _memory.GetString(address, _state.CX);
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
        switch (_state.AL) {
            case 0x00: {
                _state.AL = 0x1A; // Function supported
                _state.BL = _biosDataArea.DisplayCombinationCode; // Primary display
                _state.BH = 0x00; // No secondary display
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 1A {MethodName} - Get: DCC 0x{Dcc:X2}",
                        nameof(VgaBios), nameof(GetSetDisplayCombinationCode), _state.BL);
                }
                break;
            }
            case 0x01: {
                _state.AL = 0x1A; // Function supported
                _biosDataArea.DisplayCombinationCode = _state.BL;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 1A {MethodName} - Set: DCC 0x{Dcc:X2}",
                        nameof(VgaBios), nameof(GetSetDisplayCombinationCode), _state.BL);
                }
                break;
            }
            default: {
                throw new NotSupportedException($"AL=0x{_state.AL:X2} is not a valid subFunction for INT 10 1A");
            }
        }
    }

    /// <inheritdoc />
    public void VideoSubsystemConfiguration() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT 10 12 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(LoadFontInfo), _state.BL);
        }
        switch (_state.BL) {
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
                throw new NotSupportedException($"BL=0x{_state.BL:X2} is not a valid subFunction for INT 10 12");
        }
    }

    /// <inheritdoc />
    public void LoadFontInfo() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT 10 11 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(LoadFontInfo), _state.AL);
        }
        switch (_state.AL) {
            case 0x00:
                _vgaFunctions.LoadUserFont(_state.ES, _state.BP, _state.CX, _state.DX, _state.BL, _state.BH);
                break;
            case 0x01:
                _vgaFunctions.LoadFont(Fonts.VgaFont14, 0x100, 0, _state.BL, 14);
                break;
            case 0x02:
                _vgaFunctions.LoadFont(Fonts.VgaFont8, 0x100, 0, _state.BL, 8);
                break;
            case 0x03:
                SetBlockSpecifier(_state.BL);
                break;
            case 0x04:
                _vgaFunctions.LoadFont(Fonts.VgaFont16, 0x100, 0, _state.BL, 16);
                break;
            case 0x10:
                _vgaFunctions.LoadUserFont(_state.ES, _state.BP, _state.CX, _state.DX, _state.BL, _state.BH);
                _vgaFunctions.SetScanLines(_state.BH);
                break;
            case 0x11:
                _vgaFunctions.LoadFont(Fonts.VgaFont14, 0x100, 0, _state.BL, 14);
                _vgaFunctions.SetScanLines(14);
                break;
            case 0x12:
                _vgaFunctions.LoadFont(Fonts.VgaFont8, 0x100, 0, _state.BL, 8);
                _vgaFunctions.SetScanLines(8);
                break;
            case 0x14:
                _vgaFunctions.LoadFont(Fonts.VgaFont16, 0x100, 0, _state.BL, 16);
                _vgaFunctions.SetScanLines(16);
                break;
            case 0x20:
                _vgaFunctions.LoadUserCharacters8X8(_state.ES, _state.BP);
                break;
            case 0x21:
                _vgaFunctions.LoadUserGraphicsCharacters(_state.ES, _state.BP, _state.CL, _state.BL, _state.DL);
                break;
            case 0x22:
                _vgaFunctions.LoadRom8X14Font(_state.BL, _state.DL);
                break;
            case 0x23:
                _vgaFunctions.LoadRom8X8Font(_state.BL, _state.DL);
                break;
            case 0x24:
                _vgaFunctions.LoadGraphicsRom8X16Font(_state.BL, _state.DL);
                break;
            case 0x30:
                SegmentedAddress address = _vgaFunctions.GetFontAddress(_state.BH);
                _state.ES = address.Segment;
                _state.BP = address.Offset;
                _state.CX = (ushort)(_biosDataArea.CharacterHeight & 0xFF);
                _state.DL = _biosDataArea.ScreenRows;
                break;

            default:
                throw new NotSupportedException($"AL=0x{_state.AL:X2} is not a valid subFunction for INT 10 11");
        }
    }

    /// <inheritdoc />
    public void SetPaletteRegisters() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 10 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(SetPaletteRegisters), _state.AL);
        }
        switch (_state.AL) {
            case 0x00:
                _vgaFunctions.SetEgaPaletteRegister(_state.BL, _state.BH);
                break;
            case 0x01:
                _vgaFunctions.SetOverscanBorderColor(_state.BH);
                break;
            case 0x02:
                _vgaFunctions.SetAllPaletteRegisters(_state.ES, _state.DX);
                break;
            case 0x03:
                _vgaFunctions.ToggleIntensity((_state.BL & 1) != 0);
                break;
            case 0x07:
                if (_state.BL > 0xF) {
                    return;
                }
                _state.BH = _vgaFunctions.ReadPaletteRegister(_state.BL);
                break;
            case 0x08:
                _state.BH = _vgaFunctions.GetOverscanBorderColor();
                break;
            case 0x09:
                _vgaFunctions.GetAllPaletteRegisters(_state.ES, _state.DX);
                break;
            case 0x10:
                _vgaFunctions.WriteToDac(_state.BL, _state.DH, _state.CH, _state.CL);
                break;
            case 0x12:
                _vgaFunctions.WriteToDac(_state.ES, _state.DX, _state.BL, _state.CX);
                break;
            case 0x13:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 10 {MethodName} - Select color page, mode '{Mode}', value 0x{Value:X2} ",
                        nameof(VgaBios), nameof(SetPaletteRegisters), _state.BL == 0 ? "set Mode Control register bit 7" : "set color select register", _state.BH);
                }
                if (_state.BL == 0) {
                    _vgaFunctions.SetP5P4Select((_state.BH & 1) != 0);
                } else {
                    _vgaFunctions.SetColorSelectRegister(_state.BH);
                }
                break;
            case 0x15:
                byte[] rgb = _vgaFunctions.ReadFromDac((byte)_state.BX, 1);
                _state.DH = rgb[0];
                _state.CH = rgb[1];
                _state.CL = rgb[2];
                break;
            case 0x17:
                _vgaFunctions.ReadFromDac(_state.ES, _state.DX, (byte)_state.BX, _state.CX);
                break;
            case 0x18:
                _vgaFunctions.WriteToPixelMask(_state.BL);
                break;
            case 0x19:
                _state.BL = _vgaFunctions.ReadPixelMask();
                break;
            case 0x1A:
                _state.BX = _vgaFunctions.ReadColorPageState();
                break;
            case 0x1B:
                _vgaFunctions.PerformGrayScaleSumming(_state.BL, _state.CX);
                break;
            default:
                throw new NotSupportedException($"0x{_state.AL:X2} is not a valid palette register subFunction");
        }
    }

    /// <inheritdoc />
    public void GetVideoState() {
        _state.BH = _biosDataArea.CurrentVideoPage;
        _state.AL = (byte)(_biosDataArea.VideoMode | _biosDataArea.VideoCtl & 0x80);
        _state.AH = (byte)_biosDataArea.ScreenColumns;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0F {MethodName} - Page {Page}, mode {Mode}, columns {Columns}",
                nameof(VgaBios), nameof(GetVideoState), _state.BH, _state.AL, _state.AH);
        }
    }

    /// <inheritdoc />
    public void WriteTextInTeletypeMode() {
        CharacterPlusAttribute characterPlusAttribute = new((char)_state.AL, _state.BL, false);
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(_biosDataArea.CurrentVideoPage);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0E {MethodName} - {Character} Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteTextInTeletypeMode), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        _vgaFunctions.WriteTextInTeletypeMode(characterPlusAttribute);
    }

    /// <inheritdoc />
    public void SetColorPaletteOrBackGroundColor() {
        switch (_state.BH) {
            case 0x00:
                _vgaFunctions.SetBorderColor(_state.BL);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 0B {MethodName} - Set border color {Color}",
                        nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), _state.BL);
                }
                break;
            case 0x01:
                _vgaFunctions.SetPalette(_state.BL);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 0B {MethodName} - Set palette id {PaletteId}",
                        nameof(VgaBios), nameof(SetColorPaletteOrBackGroundColor), _state.BL);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(_state.BH), _state.BH, $"INT 10: {nameof(SetColorPaletteOrBackGroundColor)} Invalid subFunction 0x{_state.BH:X2}");
        }
    }

    /// <inheritdoc />
    public void WriteCharacterAtCursor() {
        CharacterPlusAttribute characterPlusAttribute = new((char)_state.AL, _state.BL, false);
        byte currentVideoPage = _state.BH;
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(currentVideoPage);
        int count = _state.CX;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0A {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }

        _vgaFunctions.WriteCharacterAtCursor(characterPlusAttribute, currentVideoPage, count);
    }

    /// <inheritdoc />
    public void WriteCharacterAndAttributeAtCursor() {
        CharacterPlusAttribute characterPlusAttribute = new((char)_state.AL, _state.BL, true);
        byte currentVideoPage = _state.BH;
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(currentVideoPage);
        int count = _state.CX;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 09 {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAndAttributeAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        _vgaFunctions.WriteCharacterAtCursor(characterPlusAttribute, currentVideoPage, count);
    }

    /// <inheritdoc />
    public void ReadCharacterAndAttributeAtCursor() {
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(_state.BH);
        CharacterPlusAttribute characterPlusAttribute = _vgaFunctions.ReadChar(cursorPosition);
        _state.AL = (byte)characterPlusAttribute.Character;
        _state.AH = characterPlusAttribute.Attribute;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 08 {MethodName} - Character '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(ReadCharacterAndAttributeAtCursor), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
    }

    /// <inheritdoc />
    public void ScrollPageDown() {
        _vgaFunctions.VerifyScroll(-1, _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 07 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageDown), _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        }
    }

    /// <inheritdoc />
    public void ScrollPageUp() {
        _vgaFunctions.VerifyScroll(1, _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 06 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageUp), _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        }
    }

    /// <inheritdoc />
    public void SelectActiveDisplayPage() {
        byte page = _state.AL;
        int address = _vgaFunctions.SetActivePage(page);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 05 {MethodName} - page {Page}, address {Address:X4}",
                nameof(VgaBios), nameof(SelectActiveDisplayPage), page, address);
        }
    }

    /// <inheritdoc />
    public void GetCursorPosition() {
        byte page = _state.BH;
        CursorPosition cursorPosition = _vgaFunctions.GetCursorPosition(page);
        _state.CX = _biosDataArea.CursorType;
        _state.DL = (byte)cursorPosition.X;
        _state.DH = (byte)cursorPosition.Y;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 03 {MethodName} - cursor position {X},{Y} on page {Page} type {Type}",
                nameof(VgaBios), nameof(GetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page, _state.CX);
        }
    }

    /// <inheritdoc />
    public void SetCursorPosition() {
        CursorPosition cursorPosition = new(_state.DL, _state.DH, _state.BH);
        _vgaFunctions.SetCursorPosition(cursorPosition);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 02 {MethodName} - cursor position {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(SetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
    }

    /// <inheritdoc />
    public void SetCursorType() {
        _vgaFunctions.SetCursorShape(_state.CX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 01 {MethodName} - CX: {CX}",
                nameof(VgaBios), nameof(SetCursorType), _state.CX);
        }
    }

    /// <inheritdoc />
    public void ReadDot() {
        _state.AL = _vgaFunctions.ReadPixel(_state.CX, _state.DX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0D {MethodName} - pixel at {X},{Y} = 0x{Pixel:X2}",
                nameof(VgaBios), nameof(ReadDot), _state.CX, _state.DX, _state.AL);
        }
    }

    /// <inheritdoc />
    public void WriteDot() {
        _vgaFunctions.WritePixel(_state.AL, _state.CX, _state.DX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0C {MethodName} - write {Pixel} at {X},{Y}",
                nameof(VgaBios), nameof(WriteDot), _state.AL, _state.CX, _state.DX);
        }
    }

    /// <inheritdoc />
    public void SetVideoMode() {
        int modeId = _state.AL & 0x7F;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_biosDataArea.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if ((_state.AL & 0x80) != 0) {
            flags |= ModeFlags.NoClearMem;
        }

        // Set AL
        if (modeId > 7) {
            _state.AL = 0x20;
        } else if (modeId == 6) {
            _state.AL = 0x3F;
        } else {
            _state.AL = 0x30;
        }
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 00 {MethodName} - mode {ModeId:X2}, {Flags}",
                nameof(VgaBios), nameof(SetVideoMode), modeId, flags);
        }
        _vgaFunctions.VgaSetMode(modeId, flags);
        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information("VGA BIOS mode {Mode:X2} set complete", modeId);
        }
    }

    /// <summary>
    ///     Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = _state.AH;
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
        _state.AX = _state.BX = _state.CX = _state.DX = 0;
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
        _state.AL = 0x12;
    }

    private void DisplaySwitch() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 35 {MethodName} - Ignored",
                nameof(VgaBios), nameof(DisplaySwitch));
        }
        _state.AL = 0x12;
    }

    private void CursorEmulation() {
        bool enabled = (_state.AL & 0x01) == 0;
        _vgaFunctions.CursorEmulation(enabled);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 34 {MethodName} - {Result}",
                nameof(VgaBios), nameof(CursorEmulation), enabled ? "Enabled" : "Disabled");
        }
        _state.AL = 0x12;
    }

    private void SummingToGrayScales() {
        bool enabled = (_state.AL & 0x01) == 0;
        _vgaFunctions.SummingToGrayScales(enabled);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 33 {MethodName} - {Result}",
                nameof(VgaBios), nameof(SummingToGrayScales), enabled ? "Enabled" : "Disabled");
        }
        _state.AL = 0x12;
    }

    private void VideoEnableDisable() {
        _vgaFunctions.EnableVideoAddressing((_state.AL & 1) == 0);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 32 {MethodName} - {Result}",
                nameof(VgaBios), nameof(VideoEnableDisable), (_state.AL & 0x01) == 0 ? "Enabled" : "Disabled");
        }
        _state.AL = 0x12;
    }

    private void DefaultPaletteLoading() {
        _vgaFunctions.DefaultPaletteLoading((_state.AL & 1) != 0);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 31 {MethodName} - 0x{Al:X2}",
                nameof(VgaBios), nameof(DefaultPaletteLoading), _state.AL);
        }
        _state.AL = 0x12;
    }

    private void SelectScanLines() {
        int lines = _state.AL switch {
            0x00 => 200,
            0x01 => 350,
            0x02 => 400,
            _ => throw new NotSupportedException($"AL=0x{_state.AL:X2} is not a valid subFunction for INT 10 12 30")
        };
        _vgaFunctions.SelectScanLines(lines);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 30 {MethodName} - {Lines} lines",
                nameof(VgaBios), nameof(SelectScanLines), lines);
        }
        _state.AL = 0x12;
    }

    private void EgaVgaInformation() {
        _state.BH = (byte)(_vgaFunctions.GetColorMode() ? 0x01 : 0x00);
        _state.BL = 0x03;
        _state.CX = _vgaFunctions.GetFeatureSwitches();
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 10 {MethodName} - ColorMode 0x{ColorMode:X2}, Memory: 0x{Memory:X2}, FeatureSwitches: 0x{FeatureSwitches:X2}",
                nameof(VgaBios), nameof(EgaVgaInformation), _state.BH, _state.BL, _state.CX);
        }
    }

    private void SetBlockSpecifier(byte fontBlock) {
        _vgaFunctions.SetFontBlockSpecifier(fontBlock);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, SetVideoMode));
        _dispatchTable.Add(0x01, new Callback(0x01, SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, SetCursorPosition));
        _dispatchTable.Add(0x03, new Callback(0x03, GetCursorPosition));
        _dispatchTable.Add(0x04, new Callback(0x04, ReadLightPenPosition));
        _dispatchTable.Add(0x05, new Callback(0x05, SelectActiveDisplayPage));
        _dispatchTable.Add(0x06, new Callback(0x06, ScrollPageUp));
        _dispatchTable.Add(0x07, new Callback(0x07, ScrollPageDown));
        _dispatchTable.Add(0x08, new Callback(0x08, ReadCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x09, new Callback(0x09, WriteCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x0A, new Callback(0x0A, WriteCharacterAtCursor));
        _dispatchTable.Add(0x0B, new Callback(0x0B, SetColorPaletteOrBackGroundColor));
        _dispatchTable.Add(0x0C, new Callback(0x0C, WriteDot));
        _dispatchTable.Add(0x0D, new Callback(0x0D, ReadDot));
        _dispatchTable.Add(0x0E, new Callback(0x0E, WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, GetVideoState));
        _dispatchTable.Add(0x10, new Callback(0x10, SetPaletteRegisters));
        _dispatchTable.Add(0x11, new Callback(0x11, LoadFontInfo));
        _dispatchTable.Add(0x12, new Callback(0x12, VideoSubsystemConfiguration));
        _dispatchTable.Add(0x13, new Callback(0x13, WriteString));
        _dispatchTable.Add(0x1A, new Callback(0x1A, GetSetDisplayCombinationCode));
        _dispatchTable.Add(0x1B, new Callback(0x1B, () => GetFunctionalityInfo()));
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