using Spice86.Core.DI;

namespace Spice86.Core.Emulator.InterruptHandlers.Vga;

using Serilog;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Fonts;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;
using Spice86.Logging;

public class VideoBiosInt10Handler : InterruptHandler {
    private readonly ILoggerService _loggerService;
    private readonly byte _currentDisplayPage = 0;
    private readonly byte _numberOfScreenColumns = 80;
    private readonly VgaCard _vgaCard;

    public VideoBiosInt10Handler(Machine machine, ILoggerService loggerService, VgaCard vgaCard) : base(machine) {
        _loggerService = loggerService;
        _vgaCard = vgaCard;
        FillDispatchTable();
    }

    public void GetBlockOfDacColorRegisters() {
        ushort firstRegisterToGet = _state.BX;
        ushort numberOfColorsToGet = _state.CX;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("GET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToGet}, getting {@NumberOfColorsToGet} colors, values are to be stored at address {@EsDx}", ConvertUtils.ToHex(firstRegisterToGet), numberOfColorsToGet, ConvertUtils.ToSegmentedAddressRepresentation(_state.ES, _state.DX));
        }

        uint colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DX);
        _vgaCard.GetBlockOfDacColorRegisters(firstRegisterToGet, numberOfColorsToGet, colorValuesAddress);
    }

    public override byte Index => 0x10;

    public void GetSetPaletteRegisters() {
        byte op = _state.AL;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("GET/SET PALETTE REGISTERS {@Operation}", ConvertUtils.ToHex8(op));
        }

        if (op == 0x12) {
            SetBlockOfDacColorRegisters();
        } else if (op == 0x17) {
            GetBlockOfDacColorRegisters();
        } else {
            throw new UnhandledOperationException(_machine, $"Unhandled operation for get/set palette registers op={ConvertUtils.ToHex8(op)}");
        }
    }

    public byte VideoModeValue {
        get {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("GET VIDEO MODE");
            }
            return _machine.Bios.VideoMode;
        }
    }

    public void GetVideoStatus() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("GET VIDEO STATUS");
        }
        _state.AH = _numberOfScreenColumns;
        _state.AL = VideoModeValue;
        _state.BH = _currentDisplayPage;
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    public void ScrollPageUp() {
        byte scrollAmount = _state.AL;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SCROLL PAGE UP BY AMOUNT {@ScrollAmount}", ConvertUtils.ToHex8(scrollAmount));
        }
    }

    public void SetBlockOfDacColorRegisters() {
        ushort firstRegisterToSet = _state.BX;
        ushort numberOfColorsToSet = _state.CX;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToSet}, setting {@NumberOfColorsToSet} colors, values are from address {@EsDx}", ConvertUtils.ToHex(firstRegisterToSet), numberOfColorsToSet, ConvertUtils.ToSegmentedAddressRepresentation(_state.ES, _state.DX));
        }

        uint colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DX);
        _vgaCard.SetBlockOfDacColorRegisters(firstRegisterToSet, numberOfColorsToSet, colorValuesAddress);
    }

    public void SetColorPalette() {
        byte colorId = _state.BH;
        byte colorValue = _state.BL;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET COLOR PALETTE {@ColorId}, {@ColorValue}", colorId, colorValue);
        }
    }

    public void SetCursorPosition() {
        byte cursorPositionRow = _state.DH;
        byte cursorPositionColumn = _state.DL;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET CURSOR POSITION, {@Row}, {@Column}", ConvertUtils.ToHex8(cursorPositionRow), ConvertUtils.ToHex8(cursorPositionColumn));
        }
    }

    public void SetCursorType() {
        ushort cursorStartEnd = _state.CX;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET CURSOR TYPE, SCAN LINE START END IS {@CursorStartEnd}", ConvertUtils.ToHex(cursorStartEnd));
        }
    }

    public void SetVideoMode() {
        byte videoMode = _state.AL;
        SetVideoModeValue(videoMode);
    }

    public void SetVideoModeValue(byte mode) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET VIDEO MODE {@VideoMode}", ConvertUtils.ToHex8(mode));
        }
        _machine.Bios.VideoMode = mode;
        _vgaCard.SetVideoModeValue(mode);
    }

    public void WriteTextInTeletypeMode() {
        byte chr = _state.AL;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("Write Text in Teletype Mode ascii code {@AsciiCode}, chr {@Character}", ConvertUtils.ToHex(chr), ConvertUtils.ToChar(chr));
        }
        Console.Out.Write(ConvertUtils.ToChar(chr));
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, SetVideoMode));
        _dispatchTable.Add(0x01, new Callback(0x01, SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, SetCursorPosition));
        _dispatchTable.Add(0x06, new Callback(0x06, ScrollPageUp));
        _dispatchTable.Add(0x0B, new Callback(0x0B, SetColorPalette));
        _dispatchTable.Add(0x0E, new Callback(0x0E, WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, GetVideoStatus));
        _dispatchTable.Add(0x10, new Callback(0x10, GetSetPaletteRegisters));
        _dispatchTable.Add(0x11, new Callback(0x11, CharacterGeneratorRoutine));
        _dispatchTable.Add(0x12, new Callback(0x12, VideoSubsystemConfiguration));
        _dispatchTable.Add(0x1A, new Callback(0x1A, VideoDisplayCombination));
    }

    private void CharacterGeneratorRoutine() {
        switch (_state.AL) {
            case 0x30:
                GetFontInformation();
                break;

            default:
                throw new NotImplementedException($"Video command 11{_state.AL:X2}h not implemented.");
        }
    }

    private void GetFontInformation() {
        SegmentedAddress address = _state.BH switch {
            0x00 => new SegmentedAddress(_memory.GetUint16(0x1F * 4 + 2), _memory.GetUint16(0x1F * 4)),
            0x01 => new SegmentedAddress(_memory.GetUint16(0x43 * 4 + 2), _memory.GetUint16(0x43 * 4)),
            0x02 => _vgaCard.GetFontAddress(FontType.Ega8X14),
            0x03 => _vgaCard.GetFontAddress(FontType.Ibm8X8),
            0x04 => _vgaCard.GetFontAddress(FontType.Ibm8X8) + (128 * 8), // 2nd half
            0x05 => throw new NotImplementedException("No 9x14 font available"),
            0x06 => _vgaCard.GetFontAddress(FontType.Vga8X16),
            0x07 => throw new NotImplementedException("No 9x16 font available"),
            _ => throw new NotImplementedException($"Video command 1130_{_state.BH:X2}h not implemented.")
        };

        _state.ES = address.Segment;
        _state.BP = address.Offset;
        _state.CX = _machine.Bios.CharacterPointHeight;
        _state.DL = _machine.Bios.ScreenRows;
    }

    private void VideoDisplayCombination() {
        byte op = _state.AL;
        switch (op) {
            case 0:
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information("GET VIDEO DISPLAY COMBINATION");
                }
                // VGA with analog color display
                _state.BX = 0x08;
                break;
            case 1:
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information("SET VIDEO DISPLAY COMBINATION");
                }
                throw new UnhandledOperationException(_machine, "Unimplemented");
            default:
                throw new UnhandledOperationException(_machine,
                    $"Unhandled operation for videoDisplayCombination op={ConvertUtils.ToHex8(op)}");
        }
        _state.AL = 0x1A;
        _state.AH = 0x00;
    }

    private void VideoSubsystemConfiguration() {
        byte op = _state.BL;
        switch (op) {
            case 0x0:
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information("UNKNOWN!");
                }
                break;
            case 0x10:
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information("GET VIDEO CONFIGURATION INFORMATION");
                }
                // color
                _state.BH = 0;
                // 64k of vram
                _state.BL = 0;
                // From dosbox source code ...
                _state.CH = 0;
                _state.CL = 0x09;
                break;
            default:
                throw new UnhandledOperationException(_machine,
                    $"Unhandled operation for videoSubsystemConfiguration op={ConvertUtils.ToHex8(op)}");
        }
    }
}
