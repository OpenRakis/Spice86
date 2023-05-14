namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

public class VgaBios : InterruptHandler, IVgaInterrupts {
    public const ushort GraphicsSegment = 0xA000;
    public const ushort ColorTextSegment = 0xB800;
    public const ushort MonochromeTextSegment = 0xB000;

    private readonly Bios _bios;
    private readonly ILoggerService _logger;
    private readonly VgaRom _vgaRom;
    private VgaMode _currentVgaMode;
    private readonly VgaFunctions _vgaFunctions;

    public VgaBios(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _bios = _machine.Bios;
        _vgaRom = machine.VgaRom;
        _logger = loggerService.WithLogLevel(LogEventLevel.Debug);
        _logger.Debug("Initializing VGA BIOS");
        FillDispatchTable();

        InitializeBiosArea();
        _vgaFunctions = new VgaFunctions(machine.Memory, machine.IoPortDispatcher);
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

    /// <summary>
    ///     The interrupt vector this class handles.
    /// </summary>
    public override byte Index => 0x10;

    public void WriteString() {
        CursorPosition cursorPosition = new(_state.DL, _state.DH, _state.BH);
        ushort count = _state.CX;
        ushort offset = _state.BP;
        byte attribute = _state.BL;
        bool includeAttributes = (_state.AL & 0x02) != 0;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            uint address = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BP);
            string str = _memory.GetString(address, _state.CX);
            _logger.Debug("{ClassName} INT 10 13 {MethodName}: {String} at {X},{Y} attribute: 0x{Attribute:X2}",
                nameof(VgaBios), nameof(WriteString), str, cursorPosition.X, cursorPosition.Y, includeAttributes ? "included" : attribute);
        }
        while (count-- > 0) {
            char character = (char)_memory.UInt8[_state.ES, offset++];
            if (includeAttributes) {
                attribute = _memory.UInt8[_state.ES, offset++];
            }
            CharacterPlusAttribute characterPlusAttribute = new(character, attribute, true);
            cursorPosition = WriteTeletype(cursorPosition, characterPlusAttribute);
        }

        if ((_state.AL & 0x01) != 0) {
            SetCursorPosition(cursorPosition);
        }
    }

    public VideoFunctionalityInfo GetFunctionalityInfo() {
        throw new NotImplementedException();
    }

    public void GetSetDisplayCombinationCode() {
        switch (_state.AL) {
            case 0x00: {
                _state.AL = 0x1A; // Function supported
                _state.BL = _bios.DisplayCombinationCode; // Primary display
                _state.BH = 0x00; // No secondary display
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 1A {MethodName} - Get: DCC 0x{Dcc:X2}",
                        nameof(VgaBios), nameof(GetSetDisplayCombinationCode), _state.BL);
                }
                break;
            }
            case 0x01: {
                _state.AL = 0x1A; // Function supported
                _bios.DisplayCombinationCode = _state.BL;
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
        _bios.VideoCtl = (byte)(_bios.VideoCtl & ~0x01 | _state.AL & 0x01);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 34 {MethodName} - {Result}",
                nameof(VgaBios), nameof(CursorEmulation), (_state.AL & 0x01) == 0 ? "Enabled" : "Disabled");
        }
        _state.AL = 0x12;
    }

    private void SummingToGrayScales() {
        byte v = (byte)(_state.AL << 1 & 0x02 ^ 0x02);
        byte v2 = (byte)(_bios.ModesetCtl & ~0x02);
        _bios.ModesetCtl = (byte)(v | v2);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 33 {MethodName} - {Result}",
                nameof(VgaBios), nameof(SummingToGrayScales), (_state.AL & 0x01) == 0 ? "Enabled" : "Disabled");
        }
        _state.AL = 0x12;
    }

    private void VideoEnableDisable() {
        _vgaFunctions.EnableVideoAddressing(_state.AL);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 32 {MethodName} - {Result}",
                nameof(VgaBios), nameof(VideoEnableDisable), (_state.AL & 0x01) == 0 ? "Enabled" : "Disabled");
        }
        _state.AL = 0x12;
    }

    private void DefaultPaletteLoading() {
        byte value = (byte)((_state.AL & 0x01) << 3);
        byte videoCtl = (byte)(_bios.VideoCtl & ~0x08);
        _bios.VideoCtl = (byte)(videoCtl | value);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 31 {MethodName} - 0x{Al:X2}",
                nameof(VgaBios), nameof(DefaultPaletteLoading), _state.AL);
        }
        _state.AL = 0x12;
    }

    private void SelectScanLines() {
        byte modeSetCtl = _bios.ModesetCtl;
        byte featureSwitches = _bios.FeatureSwitches;
        int lines;
        switch (_state.AL) {
            case 0x00:
                lines = 200;
                modeSetCtl = (byte)((modeSetCtl & ~0x10) | 0x80);
                featureSwitches = (byte)((featureSwitches & ~0x0F) | 0x08);
                break;
            case 0x01:
                lines = 350;
                modeSetCtl = (byte)(modeSetCtl & ~0x90);
                featureSwitches = (byte)((featureSwitches & ~0x0f) | 0x09);
                break;
            case 0x02:
                lines = 400;
                modeSetCtl = (byte)((modeSetCtl & ~0x80) | 0x10);
                featureSwitches = (byte)((featureSwitches & ~0x0f) | 0x09);
                break;
            default:
                throw new NotSupportedException($"AL=0x{_state.AL:X2} is not a valid subFunction for INT 10 12 30");
        }
        _bios.ModesetCtl = modeSetCtl;
        _bios.FeatureSwitches = featureSwitches;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 30 {MethodName} - {Lines} lines",
                nameof(VgaBios), nameof(SelectScanLines), lines);
        }
        _state.AL = 0x12;
    }

    private void EgaVgaInformation() {
        var port = (VgaPort)_bios.CrtControllerBaseAddress;
        _state.BX = (ushort)(port == VgaPort.CrtControllerAddress ? 0x0103 : 0x0003);
        _state.CX = (ushort)(_bios.FeatureSwitches & 0x0f);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 12 10 {MethodName} - ColorMode 0x{ColorMode:X2}, Memory: 0x{Memory:X2}",
                nameof(VgaBios), nameof(EgaVgaInformation), _state.BH, _state.BL);
        }
    }

    public void LoadFontInfo() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("{ClassName} INT 10 11 {MethodName} - Sub function 0x{SubFunction:X2}",
                nameof(VgaBios), nameof(LoadFontInfo), _state.AL);
        }
        switch (_state.AL) {
            case 0x00:
                LoadUserFont(_state.ES, _state.BP, _state.CX, _state.DX, _state.BL, _state.BH);
                break;
            case 0x01:
                LoadRomMonochromeFont(_state.BL);
                break;
            case 0x02:
                LoadRom8X8DoubleDotFont(_state.BL);
                break;
            case 0x03:
                SetBlockSpecifier(_state.BL);
                break;
            case 0x04:
                LoadRom8X16Font(_state.BL);
                break;
            case 0x10:
                LoadUserFont2(_state.ES, _state.BP, _state.CX, _state.DX, _state.BL, _state.BH);
                break;
            case 0x11:
                LoadRomMonochromeFont2(_state.BL);
                break;
            case 0x12:
                LoadRom8X8DoubleDotFont2(_state.BL);
                break;
            case 0x14:
                LoadRom8X16Font2(_state.BL);
                break;
            case 0x20:
                LoadUserCharacters8X8(_state.ES, _state.BP);
                break;
            case 0x21:
                LoadUserGraphicsCharacters(_state.ES, _state.BP, (byte)_state.CX, _state.BL, _state.DL);
                break;
            case 0x22:
                LoadRom8X14Font(_state.BL, _state.DL);
                break;
            case 0x23:
                LoadRom8X8Font(_state.BL, _state.DL);
                break;
            case 0x24:
                LoadGraphicsRom8X16Font(_state.BL, _state.DL);
                break;
            case 0x30:
                SegmentedAddress address = GetFontAddress(_state.BH);
                _state.ES = address.Segment;
                _state.BP = address.Offset;
                _state.CX = (ushort)(_bios.CharacterHeight & 0xFF);
                _state.DL = _bios.ScreenRows;
                break;

            default:
                throw new NotSupportedException($"AL=0x{_state.AL:X2} is not a valid subFunction for INT 10 11");
        }
    }

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
                _vgaFunctions.ToggleIntensity(_state.BL);
                break;
            case 0x07:
                if (_state.BL > 0x14) {
                    return;
                }
                _state.BH = _vgaFunctions.ReadAttributeController(_state.BL);
                break;
            case 0x08:
                _state.BH = _vgaFunctions.GetOverscanBorderColor();
                break;
            case 0x09:
                _vgaFunctions.GetAllPaletteRegisters(_state.ES, _state.DX);
                break;
            case 0x10:
                _vgaFunctions.WriteToDac(new[] {_state.DH, _state.CH, _state.CL}, (byte)_state.BX, 1);
                break;
            case 0x12:
                _vgaFunctions.WriteToDac(_state.ES, _state.DX, (byte)_state.BX, _state.CX);
                break;
            case 0x13:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("{ClassName} INT 10 10 {MethodName} - Select color page, mode '{Mode}', value 0x{Value:X2} ",
                        nameof(VgaBios), nameof(SetPaletteRegisters), _state.BL == 0 ? "set Mode Control register bit 7" : "set color select register", _state.BH);
                }
                _vgaFunctions.SelectVideoDacColorPage(_state.BL, _state.BH);
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
            case 0x1a:
                _vgaFunctions.ReadVideoDacState(out byte pMode, out byte curPage);
                _state.BH = curPage;
                _state.BL = pMode;
                break;
            case 0x1b:
                _vgaFunctions.PerformGrayScaleSumming((byte)_state.BX, _state.CX);
                break;
            default:
                throw new NotSupportedException($"0x{_state.AL:X2} is not a valid palette register subFunction");
        }
    }

    public void GetVideoState() {
        _state.BH = _bios.CurrentVideoPage;
        _state.AL = (byte)(_bios.VideoMode | _bios.VideoCtl & 0x80);
        _state.AH = (byte)_bios.ScreenColumns;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0F {MethodName} - Page {Page}, mode {Mode}, columns {Columns}",
                nameof(VgaBios), nameof(GetVideoState), _state.BH, _state.AL, _state.AH);
        }
    }

    public void WriteTextInTeletypeMode() {
        CharacterPlusAttribute characterPlusAttribute = new((char)_state.AL, _state.BL, false);
        CursorPosition cursorPosition = GetCursorPosition(_bios.CurrentVideoPage);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0E {MethodName} - '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteTextInTeletypeMode), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        cursorPosition = WriteTeletype(cursorPosition, characterPlusAttribute);
        SetCursorPosition(cursorPosition);
    }

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

    public void WriteCharacterAtCursor() {
        CharacterPlusAttribute characterPlusAttribute = new((char)_state.AL, _state.BL, false);
        CursorPosition cursorPosition = GetCursorPosition(_state.BH);
        int count = _state.CX;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0A {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        while (count-- > 0) {
            WriteCharacter(cursorPosition, characterPlusAttribute);
        }
    }

    public void WriteCharacterAndAttributeAtCursor() {
        CharacterPlusAttribute characterPlusAttribute = new((char)_state.AL, _state.BL, true);
        CursorPosition cursorPosition = GetCursorPosition(_state.BH);
        int count = _state.CX;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 09 {MethodName} - {Count} times '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(WriteCharacterAndAttributeAtCursor), count, characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
        while (count-- > 0) {
            cursorPosition = WriteCharacter(cursorPosition, characterPlusAttribute);
        }
    }

    public void ReadCharacterAndAttributeAtCursor() {
        CursorPosition cursorPosition = GetCursorPosition(_state.BH);
        CharacterPlusAttribute characterPlusAttribute = ReadChar(cursorPosition);
        _state.AL = (byte)characterPlusAttribute.Character;
        _state.AH = characterPlusAttribute.Attribute;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 08 {MethodName} - Character '{Character}' Attribute 0x{Attribute:X2} at {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(ReadCharacterAndAttributeAtCursor), characterPlusAttribute.Character, characterPlusAttribute.Attribute, cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
    }

    public void ScrollPageDown() {
        VerifyScroll(-1, _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 07 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageDown), _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        }
    }

    public void ScrollPageUp() {
        VerifyScroll(1, _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 06 {MethodName} - from {X},{Y} to {X2},{Y2}, {Lines} lines, attribute {Attribute}",
                nameof(VgaBios), nameof(ScrollPageUp), _state.CL, _state.CH, _state.DL, _state.DH, _state.AL, _state.BH);
        }
    }

    public void SelectActiveDisplayPage() {
        SetActivePage(_state.AL);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 05 {MethodName} - page {Page}",
                nameof(VgaBios), nameof(SelectActiveDisplayPage), _state.AL);
        }
    }

    public void GetCursorPosition() {
        _state.CX = _bios.CursorType;
        CursorPosition cursorPosition = GetCursorPosition(_state.BH);
        _state.DL = (byte)cursorPosition.X;
        _state.DH = (byte)cursorPosition.Y;
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 03 {MethodName} - cursor position {X},{Y} on page {Page} type {Type}",
                nameof(VgaBios), nameof(GetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page, _state.CX);
        }
    }

    public void SetCursorPosition() {
        CursorPosition cursorPosition = new(_state.DL, _state.DH, _state.BH);
        SetCursorPosition(cursorPosition);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 02 {MethodName} - cursor position {X},{Y} on page {Page}",
                nameof(VgaBios), nameof(SetCursorPosition), cursorPosition.X, cursorPosition.Y, cursorPosition.Page);
        }
    }

    public void SetCursorType() {
        SetCursorShape(_state.CX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 01 {MethodName} - CX: {CX}",
                nameof(VgaBios), nameof(SetCursorType), _state.CX);
        }
    }

    public void ReadDot() {
        _state.AL = vgafb_read_pixel(_state.CX, _state.DX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0D {MethodName} - pixel at {X},{Y} = 0x{Pixel:X2}",
                nameof(VgaBios), nameof(ReadDot), _state.CX, _state.DX, _state.AL);
        }
    }

    public void WriteDot() {
        vgafb_write_pixel(_state.AL, _state.CX, _state.DX);
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10 0C {MethodName} - write {Pixel} at {X},{Y}",
                nameof(VgaBios), nameof(WriteDot), _state.AL, _state.CX, _state.DX);
        }
    }

    public void SetVideoMode() {
        int modeId = _state.AL & 0x7F;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_bios.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
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
        VgaSetMode(modeId, flags);
    }

    private CursorPosition WriteTeletype(CursorPosition cursorPosition, CharacterPlusAttribute characterPlusAttribute) {
        switch (characterPlusAttribute.Character) {
            case (char)7:
                //FIXME should beep
                break;
            case (char)8:
                if (cursorPosition.X > 0) {
                    cursorPosition.X--;
                }
                break;
            case '\r':
                cursorPosition.X = 0;
                break;
            case '\n':
                cursorPosition.Y++;
                break;
            default:
                cursorPosition = WriteCharacter(cursorPosition, characterPlusAttribute);
                break;
        }

        // Do we need to scroll ?
        ushort numberOfRows = _bios.ScreenRows;
        if (cursorPosition.Y > numberOfRows) {
            cursorPosition.Y--;

            CursorPosition position = new(0, 0, cursorPosition.Page);
            Area area = new(_bios.ScreenColumns, numberOfRows + 1);
            Scroll(position, area, 1, new CharacterPlusAttribute(' ', 0, false));
        }
        return cursorPosition;
    }

    private void Scroll(CursorPosition position, Area area, int lines, CharacterPlusAttribute characterPlusAttribute) {
        if (lines == 0) {
            // Clear window
            ClearChars(position, area, characterPlusAttribute);
        } else if (lines > 0) {
            // Scroll the window up (eg, from page down key)
            area.Height -= lines;
            MoveChars(position, area, lines);

            position.Y += area.Height;
            area.Height = lines;
            ClearChars(position, area, characterPlusAttribute);
        } else {
            // Scroll the window down (eg, from page up key)
            position.Y -= lines;
            area.Height += lines;
            MoveChars(position, area, lines);

            position.Y += lines;
            area.Height = -lines;
            ClearChars(position, area, characterPlusAttribute);
        }
    }

    private void MoveChars(CursorPosition dest, Area area, int lines) {
        VgaMode vgaMode = _currentVgaMode;

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            GraphicalMoveChars(vgaMode, dest, area, lines);
            return;
        }

        int stride = _bios.ScreenColumns * 2;
        ushort destinationAddress = TextAddress(dest), sourceAddress = (ushort)(destinationAddress + lines * stride);
        MemMoveStride(vgaMode.StartSegment, destinationAddress, sourceAddress, area.Width * 2, stride, (ushort)area.Height);
    }

    private void ClearChars(CursorPosition startPosition, Area area, CharacterPlusAttribute characterPlusAttribute) {
        VgaMode vgaMode = _currentVgaMode;

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            GraphicalClearChars(vgaMode, startPosition, area, characterPlusAttribute);
            return;
        }

        int attribute = (characterPlusAttribute.UseAttribute ? characterPlusAttribute.Attribute : 0x07) << 8 | characterPlusAttribute.Character;
        int stride = _bios.ScreenColumns * 2;
        ushort offset = TextAddress(startPosition);
        for (int lines = area.Height; lines > 0; lines--, offset += (ushort)stride) {
            _vgaFunctions.MemSet16(vgaMode.StartSegment, offset, (ushort)attribute, area.Width * 2);
        }
    }

    private void GraphicalMoveChars(VgaMode vgaMode, CursorPosition destination, Area area, int lines) {
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = area.Width * 8;
        operation.Width = destination.X * 8;
        int characterHeight = _bios.CharacterHeight;
        operation.Y = area.Height * characterHeight;
        operation.Height = destination.Y * characterHeight;
        operation.Lines = operation.Y + lines * characterHeight;
        operation.Action = Action.MemMove;
        HandleGraphicsOperation(operation);
    }

    private void LoadGraphicsRom8X16Font(byte rowSpecifier, byte userSpecified) {
        SegmentedAddress address = _vgaRom.VgaFont16Address;
        LoadGraphicsFont(address.Segment, address.Offset, 16, rowSpecifier, userSpecified);
    }

    private void LoadRom8X8Font(byte rowSpecifier, byte userSpecified) {
        SegmentedAddress address = _vgaRom.VgaFont8Address;
        LoadGraphicsFont(address.Segment, address.Offset, 8, rowSpecifier, userSpecified);
    }

    private void LoadRom8X14Font(byte rowSpecifier, byte userSpecified) {
        SegmentedAddress address = _vgaRom.VgaFont14Address;
        LoadGraphicsFont(address.Segment, address.Offset, 14, rowSpecifier, userSpecified);
    }

    private void LoadUserGraphicsCharacters(ushort segment, ushort offset, byte height, byte rowSpecifier, byte userSpecified) {
        LoadGraphicsFont(segment, offset, height, rowSpecifier, userSpecified);
    }

    private void LoadGraphicsFont(ushort segment, ushort offset, byte height, byte rowSpecifier, byte userSpecified) {
        SetInterruptVectorAddress(0x43, segment, offset);
        byte rows = rowSpecifier switch {
            0 => userSpecified,
            1 => 14,
            3 => 43,
            _ => 25
        };
        _bios.ScreenRows = (byte)(rows - 1);
        _bios.CharacterHeight = height;
    }

    private void LoadUserCharacters8X8(ushort segment, ushort offset) {
        SetInterruptVectorAddress(0x1F, segment, offset);
    }

    private void SetInterruptVectorAddress(int vector, ushort segment, ushort offset) {
        int tableOffset = 4 * vector;
        _memory.UInt16[MemoryMap.InterruptVectorSegment, (ushort)tableOffset] = offset;
        _memory.UInt16[MemoryMap.InterruptVectorSegment, (ushort)(tableOffset + 2)] = segment;
    }

    private void LoadRom8X16Font2(byte fontBlock) {
        _vgaFunctions.LoadFont(VgaRom.VgaFont16, 0x100, 0, fontBlock, 16);
        SetScanLines(16);
    }

    private void LoadRom8X8DoubleDotFont2(byte fontBlock) {
        _vgaFunctions.LoadFont(VgaRom.VgaFont8, 0x100, 0, fontBlock, 8);
        SetScanLines(8);
    }

    private void LoadRomMonochromeFont2(byte fontBlock) {
        _vgaFunctions.LoadFont(VgaRom.VgaFont14, 0x100, 0, fontBlock, 14);
        SetScanLines(14);
    }

    private void LoadUserFont2(ushort segment, ushort offset, ushort length, ushort start, byte fontBlock, byte height) {
        byte[] bytes = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), length);
        _vgaFunctions.LoadFont(bytes, length, start, fontBlock, height);
        SetScanLines(height);
    }

    private void SetScanLines(byte lines) {
        _vgaFunctions.SetScanLines(lines);
        _bios.CharacterHeight = lines;
        ushort vde = _vgaFunctions.get_vde();
        byte rows = (byte)(vde / lines);
        _bios.ScreenRows = (byte)(rows - 1);
        ushort columns = _bios.ScreenColumns;
        _bios.VideoPageSize = (ushort)CalculatePageSize(MemoryModel.Text, columns, rows);
        if (lines == 8) {
            SetCursorShape(0x0607);
        } else {
            SetCursorShape((ushort)(lines - 3 << 8 | lines - 2));
        }
    }

    private void LoadRom8X16Font(byte fontBlock) {
        _vgaFunctions.LoadFont(VgaRom.VgaFont16, 0x100, 0, fontBlock, 16);
    }

    private void SetBlockSpecifier(byte fontBlock) {
        _vgaFunctions.SetTextBlockSpecifier(fontBlock);
    }

    private void LoadRom8X8DoubleDotFont(byte fontBlock) {
        _vgaFunctions.LoadFont(VgaRom.VgaFont8, 0x100, 0, fontBlock, 8);
    }

    private void LoadRomMonochromeFont(byte fontBlock) {
        _vgaFunctions.LoadFont(VgaRom.VgaFont14, 0x100, 0, fontBlock, 14);
    }

    private void LoadUserFont(ushort segment, ushort offset, ushort length, ushort start, byte fontBlock, byte height) {
        byte[] bytes = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), length);
        _vgaFunctions.LoadFont(bytes, length, start, fontBlock, height);
    }

    private void SetCursorPosition(CursorPosition cursorPosition) {
        if (cursorPosition.Page > 7) {
            // Should not happen...
            return;
        }

        if (cursorPosition.Page == _bios.CurrentVideoPage) {
            // Update cursor in hardware
            _vgaFunctions.SetCursorPosition(TextAddress(cursorPosition));
        }

        // Update BIOS cursor pos
        int position = cursorPosition.Y << 8 | cursorPosition.X;
        _bios.SetCursorPosition(cursorPosition.Page, (ushort)position);
    }

    private ushort TextAddress(CursorPosition cursorPosition) {
        int stride = _bios.ScreenColumns * 2;
        int pageOffset = _bios.VideoPageSize * cursorPosition.Page;
        return (ushort)(pageOffset + cursorPosition.Y * stride + cursorPosition.X * 2);
    }

    private void GraphicalClearChars(VgaMode vgaMode, CursorPosition startPosition, Area area, CharacterPlusAttribute ca) {
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = startPosition.X * 8;
        operation.Width = area.Width * 8;
        int characterHeight = _bios.CharacterHeight;
        operation.Y = startPosition.Y * characterHeight;
        operation.Height = area.Height * characterHeight;
        operation.Pixels[0] = ca.Attribute;
        operation.Action = Action.MemSet;
        HandleGraphicsOperation(operation);
    }

    private void HandleGraphicsOperation(GraphicsOperation operation) {
        switch (operation.VgaMode.MemoryModel) {
            case MemoryModel.Planar:
                HandlePlanarGraphicsOperation(operation);
                break;
            case MemoryModel.Cga:
                HandleCgaGraphicsOperation(operation);
                break;
            case MemoryModel.Packed:
                HandlePackedGraphicsOperation(operation);
                break;
            case MemoryModel.Direct:
            case MemoryModel.Text:
            case MemoryModel.Hercules:
            case MemoryModel.NonChain4X256:
            case MemoryModel.Yuv:
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), $"Unsupported memory model {operation.VgaMode.MemoryModel}");
        }
    }

    private void HandleCgaGraphicsOperation(GraphicsOperation operation) {
        int bitsPerPixel = operation.VgaMode.BitsPerPixel;
        ushort offset = (ushort)(operation.Y / 2 * operation.LineLength + operation.X / 8 * bitsPerPixel);
        switch (operation.Action) {
            default:
            case Action.ReadByte:
                ReadByteOperationCga(operation, offset, bitsPerPixel);
                break;
            case Action.WriteByte:
                WriteByteOperationCga(operation, offset, bitsPerPixel);
                break;
            case Action.MemSet:
                MemSetOperationCga(operation, offset, bitsPerPixel);
                break;
            case Action.MemMove:
                MemMoveOperationCga(operation, offset, bitsPerPixel);
                break;
        }
    }

    private void MemMoveOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        ushort source = (ushort)(operation.Lines / 2 * operation.LineLength + operation.X / 8 * bitsPerPixel);
        MemMoveStride(ColorTextSegment, offset, source, operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
        MemMoveStride(ColorTextSegment, (ushort)(offset + 0x2000), (ushort)(source + 0x2000), operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
    }

    private void MemSetOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        byte data = operation.Pixels[0];
        if (bitsPerPixel == 1) {
            data = (byte)(data & 1 | (data & 1) << 1);
        }
        data &= 3;
        data |= (byte)(data << 2 | data << 4 | data << 6);
        MemSetStride(ColorTextSegment, offset, data, operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
        MemSetStride(ColorTextSegment, (ushort)(offset + 0x2000), data, operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
    }

    private void WriteByteOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        if ((operation.Y & 1) != 0) {
            offset += 0x2000;
        }
        if (bitsPerPixel == 1) {
            byte uint8 = 0;
            for (int pixel = 0; pixel < 8; pixel++) {
                uint8 |= (byte)((operation.Pixels[pixel] & 1) << 7 - pixel);
            }
            _memory.UInt8[ColorTextSegment, offset] = uint8;
        } else {
            ushort uint16 = 0;
            for (int pixel = 0; pixel < 8; pixel++) {
                uint16 |= (byte)((operation.Pixels[pixel] & 3) << (7 - pixel) * 2);
            }
            uint16 = (ushort)(uint16 << 8 | uint16 >> 8);
            _memory.UInt16[ColorTextSegment, offset] = uint16;
        }
    }

    private void ReadByteOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        if ((operation.Y & 1) != 0) {
            offset += 0x2000;
        }
        if (bitsPerPixel == 1) {
            byte uint8 = _memory.UInt8[ColorTextSegment, offset];
            int pixel;
            for (pixel = 0; pixel < 8; pixel++) {
                operation.Pixels[pixel] = (byte)(uint8 >> 7 - pixel & 1);
            }
        } else {
            ushort uint16 = _memory.UInt16[ColorTextSegment, offset];
            uint16 = (ushort)(uint16 << 8 | uint16 >> 8);
            int pixel;
            for (pixel = 0; pixel < 8; pixel++) {
                operation.Pixels[pixel] = (byte)(uint16 >> (7 - pixel) * 2 & 3);
            }
        }
    }

    private void HandlePlanarGraphicsOperation(GraphicsOperation operation) {
        ushort offset = (ushort)(operation.Y * operation.LineLength + operation.X / 8);
        int plane;
        switch (operation.Action) {
            default:
            case Action.ReadByte:
                ReadByteOperationPlanar(operation, offset);
                break;
            case Action.WriteByte:
                WriteByteOperationPlanar(operation, offset);
                break;
            case Action.MemSet:
                MemSetOperationPlanar(operation, offset);
                break;
            case Action.MemMove:
                MemMoveOperationPlanar(operation, offset);
                break;
        }
        _vgaFunctions.planar4_plane(-1);
    }

    private void MemMoveOperationPlanar(GraphicsOperation operation, ushort offset) {
        int plane;
        ushort source = (ushort)(operation.Lines * operation.LineLength + operation.X / 8);
        for (plane = 0; plane < 4; plane++) {
            _vgaFunctions.planar4_plane(plane);
            MemMoveStride(GraphicsSegment, offset, source, operation.Width / 8, operation.LineLength, operation.Height);
        }
    }

    private void MemSetOperationPlanar(GraphicsOperation operation, ushort offset) {
        int plane;
        for (plane = 0; plane < 4; plane++) {
            _vgaFunctions.planar4_plane(plane);
            byte data = (byte)((operation.Pixels[0] & 1 << plane) != 0 ? 0xFF : 0x00);
            MemSetStride(GraphicsSegment, offset, data, operation.Width / 8, operation.LineLength, operation.Height);
        }
    }

    private void WriteByteOperationPlanar(GraphicsOperation operation, ushort offset) {
        int plane;
        for (plane = 0; plane < 4; plane++) {
            _vgaFunctions.planar4_plane(plane);
            byte data = 0;
            for (int pixel = 0; pixel < 8; pixel++) {
                data |= (byte)((operation.Pixels[pixel] >> plane & 1) << 7 - pixel);
            }
            _memory.UInt8[GraphicsSegment, offset] = data;
        }
    }

    private void ReadByteOperationPlanar(GraphicsOperation operation, ushort offset) {
        int plane;
        operation.Pixels = new byte[8];
        for (plane = 0; plane < 4; plane++) {
            _vgaFunctions.planar4_plane(plane);
            byte data = _memory.UInt8[GraphicsSegment, offset];
            int pixel;
            for (pixel = 0; pixel < 8; pixel++) {
                operation.Pixels[pixel] |= (byte)((data >> 7 - pixel & 1) << plane);
            }
        }
    }

    private void MemMoveStride(ushort segment, ushort destination, ushort source, int length, int stride, int lines) {
        if (source < destination) {
            destination += (ushort)(stride * (lines - 1));
            source += (ushort)(stride * (lines - 1));
            stride = -stride;
        }
        for (; lines > 0; lines--, destination += (ushort)stride, source += (ushort)stride) {
            uint sourceAddress = MemoryUtils.ToPhysicalAddress(segment, source);
            uint destinationAddress = MemoryUtils.ToPhysicalAddress(segment, destination);
            _memory.MemCopy(sourceAddress, destinationAddress, (uint)length);
        }
    }

    private void MemSetStride(ushort segment, ushort destination, byte value, int length, int stride, int lines) {
        for (; lines > 0; lines--, destination += (ushort)stride) {
            _memory.Memset(MemoryUtils.ToPhysicalAddress(segment, destination), value, (uint)length);
        }
    }

    private CursorPosition WriteCharacter(CursorPosition cursorPosition, CharacterPlusAttribute characterPlusAttribute) {
        VgaMode vgaMode = _currentVgaMode;

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            WriteCharacterGraphics(cursorPosition, characterPlusAttribute, vgaMode);
        } else {
            ushort offset = TextAddress(cursorPosition);
            if (characterPlusAttribute.UseAttribute) {
                _memory.UInt16[vgaMode.StartSegment, offset] = (ushort)(characterPlusAttribute.Attribute << 8 | characterPlusAttribute.Character);
            } else {
                _memory.UInt16[vgaMode.StartSegment, offset] = characterPlusAttribute.Character;
            }
        }
        cursorPosition.X++;
        // Wrap at end of line.
        if (cursorPosition.X == _bios.ScreenColumns) {
            cursorPosition.X = 0;
            cursorPosition.Y++;
        }
        return cursorPosition;
    }

    private void WriteCharacterGraphics(CursorPosition cursorPosition, CharacterPlusAttribute characterPlusAttribute, VgaMode vgaMode) {
        if (cursorPosition.X >= _bios.ScreenColumns) {
            return;
        }
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = (ushort)(cursorPosition.X * 8);
        int characterHeight = _bios.CharacterHeight;
        operation.Y = (ushort)(cursorPosition.Y * characterHeight);
        byte foregroundAttribute = characterPlusAttribute.Attribute;
        bool useXor = false;
        if ((foregroundAttribute & 0x80) != 0 && vgaMode.BitsPerPixel < 8) {
            useXor = true;
            foregroundAttribute &= 0x7f;
        }
        SegmentedAddress font = GetFontAddress(characterPlusAttribute.Character);
        for (int i = 0; i < characterHeight; i++, operation.Y++) {
            byte fontLine = _memory.UInt8[font.Segment, (ushort)(font.Offset + i)];
            if (useXor) {
                operation.Action = Action.ReadByte;
                HandleGraphicsOperation(operation);
                for (int j = 0; j < 8; j++) {
                    operation.Pixels[j] ^= (byte)((fontLine & 0x80 >> j) != 0 ? foregroundAttribute : 0x00);
                }
            } else {
                for (int j = 0; j < 8; j++) {
                    operation.Pixels[j] = (byte)((fontLine & 0x80 >> j) != 0 ? foregroundAttribute : 0x00);
                }
            }
            operation.Action = Action.WriteByte;
            HandleGraphicsOperation(operation);
        }
    }

    private GraphicsOperation CreateGraphicsOperation(VgaMode vgaMode) {
        return new GraphicsOperation {
            Pixels = new byte[8],
            VgaMode = vgaMode,
            LineLength = _vgaFunctions.GetLineLength(vgaMode),
            DisplayStart = _vgaFunctions.GetDisplaystart(vgaMode),
            Width = 0,
            Height = 0,
            X = 0,
            Y = 0,
            Action = Action.ReadByte
        };
    }

    private SegmentedAddress GetFontAddress(char character) {
        int characterHeight = _bios.CharacterHeight;
        SegmentedAddress address;
        if (characterHeight == 8 && character >= 128) {
            address = GetInterruptVectorAddress(0x1F);
            character = (char)(character - 128);
        } else {
            address = GetInterruptVectorAddress(0x43);
        }
        address.Offset += (ushort)(character * characterHeight);
        return address;
    }

    private SegmentedAddress GetInterruptVectorAddress(int vector) {
        int tableOffset = 4 * vector;
        ushort segment = _memory.UInt16[MemoryMap.InterruptVectorSegment, (ushort)(tableOffset + 2)];
        ushort offset = _memory.UInt16[MemoryMap.InterruptVectorSegment, (ushort)tableOffset];
        return new SegmentedAddress(segment, offset);
    }

    private CursorPosition GetCursorPosition(byte page) {
        if (page > 7) {
            return new CursorPosition(0, 0, 0);
        }
        ushort xy = _bios.GetCursorPosition(page);
        return new CursorPosition(xy, xy >> 8, page);
    }

    private CharacterPlusAttribute ReadChar(CursorPosition cp) {
        VgaMode vgaMode = _currentVgaMode;

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            return ReadGraphicsCharacter(vgaMode, cp);
        }

        ushort offset = TextAddress(cp);
        ushort value = _memory.UInt16[vgaMode.StartSegment, offset];
        return new CharacterPlusAttribute((char)value, (byte)(value >> 8), false);
    }

    private CharacterPlusAttribute ReadGraphicsCharacter(VgaMode vgaMode, CursorPosition cursorPosition) {
        int characterHeight = _bios.CharacterHeight;
        if (cursorPosition.X >= _bios.ScreenColumns || characterHeight > 16) {
            return new CharacterPlusAttribute((char)0, 0, false);
        }

        // Read cell from screen
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.Action = Action.ReadByte;
        operation.X = (ushort)(cursorPosition.X * 8);
        operation.Y = (ushort)(cursorPosition.Y * characterHeight);

        byte foregroundAttribute = 0x00;
        const byte backgroundAttribute = 0x00;
        byte[] lines = new byte[characterHeight];

        for (byte i = 0; i < characterHeight; i++, operation.Y++) {
            byte line = 0;
            HandleGraphicsOperation(operation);
            for (byte j = 0; j < 8; j++) {
                if (operation.Pixels[j] == backgroundAttribute) {
                    continue;
                }
                line |= (byte)(0x80 >> j);
                foregroundAttribute = operation.Pixels[j];
            }
            lines[i] = line;
        }

        // Determine font
        for (char character = (char)0; character < 256; character++) {
            SegmentedAddress font = GetFontAddress(character);
            if (MemCmp(lines, font.Segment, font.Offset, characterHeight) == 0) {
                return new CharacterPlusAttribute(character, foregroundAttribute, false);
            }
        }

        return new CharacterPlusAttribute((char)0, 0, false);
    }

    private int MemCmp(IReadOnlyList<byte> bytes, ushort segment, ushort offset, int length) {
        int i = 0;
        while (length-- > 0 && i < bytes.Count) {
            int difference = bytes[i] - _memory.UInt8[segment, offset];
            if (difference != 0) {
                return difference < 0 ? -1 : 1;
            }
            i++;
            offset++;
        }
        return 0;
    }

    private void VerifyScroll(int direction, byte upperLeftX, byte upperLeftY, byte lowerRightX, byte lowerRightY, int lines, byte attribute) {
        // Verify parameters
        ushort numberOfRows = (ushort)(_bios.ScreenRows + 1);
        if (lowerRightY >= numberOfRows) {
            lowerRightY = (byte)(numberOfRows - 1);
        }
        ushort numberOfColumns = _bios.ScreenColumns;
        if (lowerRightX >= numberOfColumns) {
            lowerRightX = (byte)(numberOfColumns - 1);
        }
        int width = lowerRightX - upperLeftX + 1;
        int height = lowerRightY - upperLeftY + 1;
        if (width <= 0 || height <= 0) {
            return;
        }

        if (lines >= height) {
            lines = 0;
        }
        lines *= direction;

        // Scroll (or clear) window
        CursorPosition cursorPosition = new(upperLeftX, upperLeftY, _bios.CurrentVideoPage);
        Area area = new(width, height);
        CharacterPlusAttribute attr = new(' ', attribute, true);
        Scroll(cursorPosition, area, lines, attr);
    }

    private void SetActivePage(byte page) {
        if (page > 7) {
            return;
        }
        // Calculate memory address of start of page
        CursorPosition cursorPosition = new(0, 0, page);
        int address = TextAddress(cursorPosition);
        _vgaFunctions.SetDisplayStart(_currentVgaMode, address);

        // And change the BIOS page
        _bios.VideoPageStart = (ushort)address;
        _bios.CurrentVideoPage = page;

        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information("INT 10 Set active page {Page:X2} address {Address:X4}", page, address);
        }

        // Display the cursor, now the page is active
        SetCursorPosition(GetCursorPosition(page));
    }

    private void SetCursorShape(ushort cursorType) {
        _bios.CursorType = cursorType;
        _vgaFunctions.SetCursorShape(GetCursorShape());
    }

    private ushort GetCursorShape() {
        ushort cursorType = _bios.CursorType;
        bool emulateCursor = (_bios.VideoCtl & 1) == 0;
        if (!emulateCursor) {
            return cursorType;
        }
        byte start = (byte)(cursorType >> 8 & 0x3f);
        byte end = (byte)(cursorType & 0x1f);
        ushort characterHeight = _bios.CharacterHeight;
        if (characterHeight <= 8 || end >= 8 || start >= 0x20) {
            return cursorType;
        }
        if (end != start + 1) {
            start = (byte)((start + 1) * characterHeight / 8 - 1);
        } else {
            start = (byte)((end + 1) * characterHeight / 8 - 2);
        }
        end = (byte)((end + 1) * characterHeight / 8 - 1);
        return (ushort)(start << 8 | end);
    }

    private void InitializeBiosArea() {
        // init detected hardware BIOS Area
        // set 80x25 color (not clear from RBIL but usual)
        set_equipment_flags(0x30, 0x20);

        // Set the basic modeset options
        _bios.ModesetCtl = 0x51;
        _bios.DisplayCombinationCode = 0x08;
    }

    private void set_equipment_flags(int clear, int set) {
        _bios.EquipmentListFlags = (ushort)(_bios.EquipmentListFlags & ~clear | set);
    }

    /// <summary>
    ///     Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = _state.AH;
        // if (_logger.IsEnabled(LogEventLevel.Debug))
        _logger.Debug("{ClassName} running INT 10 operation 0x{Operation:X2}", nameof(VgaBios), operation);
        Run(operation);
    }

    private byte vgafb_read_pixel(ushort x, ushort y) {
        VgaMode vgaMode = _currentVgaMode;

        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = ALIGN_DOWN(x, 8);
        operation.Y = y;
        operation.Action = Action.ReadByte;
        HandleGraphicsOperation(operation);

        return operation.Pixels[x & 0x07];
    }

    private ushort ALIGN_DOWN(ushort value, int alignment) {
        int mask = alignment - 1;
        return (ushort)(value & ~mask);
    }

    private void vgafb_write_pixel(byte color, ushort x, ushort y) {
        VgaMode vgaMode = _currentVgaMode;

        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = ALIGN_DOWN(x, 8);
        operation.Y = y;
        operation.Action = Action.ReadByte;
        HandleGraphicsOperation(operation);

        bool useXor = (color & 0x80) != 0 && vgaMode.BitsPerPixel < 8;
        if (useXor) {
            operation.Pixels[x & 0x07] ^= (byte)(color & 0x7f);
        } else {
            operation.Pixels[x & 0x07] = color;
        }
        operation.Action = Action.WriteByte;
        HandleGraphicsOperation(operation);
    }

    public void ReadLightPenPosition() {
        _state.AX = _state.BX = _state.CX = _state.DX = 0;
    }

    private void VgaSetMode(int modeId, ModeFlags flags) {
        VideoMode videoMode = GetVideoMode(modeId);

        _vgaFunctions.SetMode(videoMode, flags);
        VgaMode vgaMode = videoMode.VgaMode;

        // Set the BIOS mem
        ushort width = vgaMode.Width;
        ushort height = vgaMode.Height;
        MemoryModel memoryModel = vgaMode.MemoryModel;
        ushort characterHeight = vgaMode.CharacterHeight;
        if (modeId < 0x100) {
            _bios.VideoMode = (byte)modeId;
        } else {
            _bios.VideoMode = 0xff;
        }

        _currentVgaMode = vgaMode;

        if (memoryModel == MemoryModel.Text) {
            _bios.ScreenColumns = (byte)width;
            _bios.ScreenRows = (byte)(height - 1);
            _bios.CursorType = 0x0607;
        } else {
            _bios.ScreenColumns = (byte)(width / vgaMode.CharacterWidth);
            _bios.ScreenRows = (byte)(height / vgaMode.CharacterHeight - 1);
            _bios.CursorType = 0x0000;
        }
        _bios.VideoPageSize = (ushort)CalculatePageSize(memoryModel, width, height);
        _bios.CrtControllerBaseAddress = (ushort)_vgaFunctions.GetCrtControllerAddress();
        _bios.CharacterHeight = characterHeight;
        _bios.VideoCtl = (byte)(0x60 | (flags.HasFlag(ModeFlags.NoClearMem) ? 0x80 : 0x00));
        _bios.FeatureSwitches = 0xF9;
        _bios.ModesetCtl &= 0x7F;
        for (int i = 0; i < 8; i++) {
            _bios.SetCursorPosition(i, 0x0000);
        }
        _bios.VideoPageStart = 0x0000;
        _bios.CurrentVideoPage = 0x00;

        // Set the ints 0x1F and 0x43
        SetInterruptVectorAddress(0x1F, _vgaRom.VgaFont8Address2.Segment, _vgaRom.VgaFont8Address2.Offset);

        SegmentedAddress address;
        switch (characterHeight) {
            case 8:
                address = _vgaRom.VgaFont8Address;
                break;
            case 14:
                address = _vgaRom.VgaFont14Address;
                break;
            case 16:
                address = _vgaRom.VgaFont16Address;
                break;
            default:
                return;
        }
        SetInterruptVectorAddress(0x43, address.Segment, address.Offset);
    }

    private static int CalculatePageSize(MemoryModel memoryModel, int width, int height) {
        return memoryModel switch {
            MemoryModel.Text => Align(width * height * 2, 2 * 1024),
            MemoryModel.Cga => 16 * 1024,
            _ => Align(width * height / 8, 8 * 1024)
        };
    }

    private static int Align(int alignment, int value) {
        int mask = alignment - 1;
        return value + mask & ~mask;
    }

    private static VideoMode GetVideoMode(int modeId) {
        if (RegisterValueSet.VgaModes.TryGetValue(modeId, out VideoMode mode)) {
            return mode;
        }

        throw new ArgumentOutOfRangeException(nameof(modeId), modeId, "Unknown mode");
    }

    private SegmentedAddress GetFontAddress(byte fontNumber) {
        SegmentedAddress address = fontNumber switch {
            0x00 => GetInterruptVectorAddress(0x1F),
            0x01 => GetInterruptVectorAddress(0x43),
            0x02 => _vgaRom.VgaFont14Address,
            0x03 => _vgaRom.VgaFont8Address,
            0x04 => _vgaRom.VgaFont8Address2,
            0x05 => _vgaRom.VgaFont14Address,
            0x06 => _vgaRom.VgaFont16Address,
            0x07 => _vgaRom.VgaFont16Address,
            _ => throw new NotSupportedException($"{fontNumber} is not a valid font number")
        };

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("{ClassName} INT 10: GetFontInformation - FontNr {FontNr}, Address: {Segment:X4}:{Offset:X4}",
                nameof(VgaBios), fontNumber, address.Segment, address.Offset);
        }

        return address;
    }

    private void HandlePackedGraphicsOperation(GraphicsOperation operation) {
        ushort destination = (ushort)(operation.Y * operation.LineLength + operation.X);
        switch (operation.Action) {
            default:
            case Action.ReadByte:
                operation.Pixels = _memory.GetData(MemoryUtils.ToPhysicalAddress(GraphicsSegment, destination), 8);
                break;
            case Action.WriteByte:
                _memory.LoadData(MemoryUtils.ToPhysicalAddress(GraphicsSegment, destination), operation.Pixels, 8);
                break;
            case Action.MemSet:
                MemSetStride(GraphicsSegment, destination, operation.Pixels[0], operation.Width, operation.LineLength, operation.Height);
                break;
            case Action.MemMove:
                ushort source = (ushort)(operation.Lines * operation.LineLength + operation.X);
                MemMoveStride(GraphicsSegment, destination, source, operation.Width, operation.LineLength, operation.Height);
                break;
        }
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

public record struct GraphicsOperation(VgaMode VgaMode, int LineLength, int DisplayStart, Action Action, int X, int Y, byte[] Pixels, int Width, int Height, int Lines);

public record struct CharacterPlusAttribute(char Character, byte Attribute, bool UseAttribute);

public record struct CursorPosition(int X, int Y, int Page);

public record struct Area(int Width, int Height);

public record struct VideoMode(VgaMode VgaMode, byte PixelMask, byte[] Palette, byte[] SequencerRegisterValues, byte MiscellaneousRegisterValue, byte[] CrtControllerRegisterValues, byte[] AttributeControllerRegisterValues, byte[] GraphicsControllerRegisterValues);

public enum Action {
    ReadByte,
    WriteByte,
    MemSet,
    MemMove
}

[Flags]
public enum ModeFlags {
    // Mode flags
    Legacy = 0x0001,
    GraySum = 0x0002,
    NoPalette = 0x0008,
    NoClearMem = 0x8000,
}

public enum MemoryModel {
    Text,
    Cga,
    Hercules,
    Planar,
    Packed,
    NonChain4X256,
    Direct,
    Yuv
}

internal enum VgaPort {
    AttributeAddress = 0x3C0,
    AttributeData = 0x3C1,
    MiscOutputWrite = 0x3C2,
    SequencerAddress = 0x3C4,
    DacPelMask = 0x3C6,
    DacAddressReadIndex = 0x3C7,
    DacAddressWriteIndex = 0x3C8,
    DacData = 0x3C9,
    MiscOutputRead = 0x3CC,
    GraphicsControllerAddress = 0x3CE,
    CrtControllerAddress = 0x3B4,
    CrtControllerAddressAlt = 0x3D4,
    InputStatus1ReadAlt = 0x3DA,
}