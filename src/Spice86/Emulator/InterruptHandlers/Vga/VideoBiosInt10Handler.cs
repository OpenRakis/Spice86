namespace Spice86.Emulator.InterruptHandlers.Vga;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.Devices.Video;
using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;
using Spice86.Utils;

public class VideoBiosInt10Handler : InterruptHandler {
    public const int BiosVideoMode = 0x49;
    public static readonly uint BIOS_VIDEO_MODE_ADDRESS = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, BiosVideoMode);
    public static readonly uint CRT_IO_PORT_ADDRESS_IN_RAM = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, MemoryMap.BiosDataAreaOffsetCrtIoPort);
    private static readonly ILogger _logger = Log.Logger.ForContext<VideoBiosInt10Handler>();
    private readonly byte _currentDisplayPage = 0;
    private readonly byte _numberOfScreenColumns = 80;
    private readonly VgaCard _vgaCard;

    public VideoBiosInt10Handler(Machine machine, VgaCard vgaCard) : base(machine) {
        this._vgaCard = vgaCard;
        FillDispatchTable();
    }

    public void GetBlockOfDacColorRegisters() {
        ushort firstRegisterToGet = _state.BX;
        ushort numberOfColorsToGet = _state.CX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToGet}, getting {@NumberOfColorsToGet} colors, values are to be stored at address {@EsDx}", ConvertUtils.ToHex(firstRegisterToGet), numberOfColorsToGet, ConvertUtils.ToSegmentedAddressRepresentation(_state.ES, _state.DX));
        }

        uint colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DX);
        _vgaCard.GetBlockOfDacColorRegisters(firstRegisterToGet, numberOfColorsToGet, colorValuesAddress);
    }

    public override byte Index => 0x10;

    public void GetSetPaletteRegisters() {
        byte op = _state.AL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET/SET PALETTE REGISTERS {@Operation}", ConvertUtils.ToHex8(op));
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
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("GET VIDEO MODE");
            }
            return _memory.GetUint8(BIOS_VIDEO_MODE_ADDRESS);
        }
    }

    public void GetVideoStatus() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("GET VIDEO STATUS");
        }
        _state.AH = _numberOfScreenColumns;
        _state.AL = VideoModeValue;
        _state.BH = _currentDisplayPage;
    }

    public void InitRam() {
        this.SetVideoModeValue(VgaCard.MODE_320_200_256);
        _memory.SetUint16(CRT_IO_PORT_ADDRESS_IN_RAM, VgaCard.CRT_IO_PORT);
    }

    public override void Run() {
        byte operation = _state.AH;
        this.Run(operation);
    }

    public void ScrollPageUp() {
        byte scrollAmount = _state.AL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SCROLL PAGE UP BY AMOUNT {@ScrollAmount}", ConvertUtils.ToHex8(scrollAmount));
        }
    }

    public void SetBlockOfDacColorRegisters() {
        ushort firstRegisterToSet = _state.BX;
        ushort numberOfColorsToSet = _state.CX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToSet}, setting {@NumberOfColorsToSet} colors, values are from address {@EsDx}", ConvertUtils.ToHex(firstRegisterToSet), numberOfColorsToSet, ConvertUtils.ToSegmentedAddressRepresentation(_state.ES, _state.DX));
        }

        uint colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DX);
        _vgaCard.SetBlockOfDacColorRegisters(firstRegisterToSet, numberOfColorsToSet, colorValuesAddress);
    }

    public void SetColorPalette() {
        byte colorId = _state.BH;
        byte colorValue = _state.BL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET COLOR PALETTE {@ColorId}, {@ColorValue}", colorId, colorValue);
        }
    }

    public void SetCursorPosition() {
        byte cursorPositionRow = _state.DH;
        byte cursorPositionColumn = _state.DL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET CURSOR POSITION, {@Row}, {@Column}", ConvertUtils.ToHex8(cursorPositionRow), ConvertUtils.ToHex8(cursorPositionColumn));
        }
    }

    public void SetCursorType() {
        ushort cursorStartEnd = _state.CX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET CURSOR TYPE, SCAN LINE START END IS {@CursorStartEnd}", ConvertUtils.ToHex(cursorStartEnd));
        }
    }

    public void SetVideoMode() {
        byte videoMode = _state.AL;
        SetVideoModeValue(videoMode);
    }

    public void SetVideoModeValue(byte mode) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET VIDEO MODE {@VideoMode}", ConvertUtils.ToHex8(mode));
        }
        _memory.SetUint8(BIOS_VIDEO_MODE_ADDRESS, mode);
        _vgaCard.SetVideoModeValue(mode);
    }

    public void WriteTextInTeletypeMode() {
        byte chr = _state.AL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Write Text in Teletype Mode ascii code {@AsciiCode}, chr {@Character}", ConvertUtils.ToHex(chr), ConvertUtils.ToChar(chr));
        }
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, this.SetVideoMode));
        _dispatchTable.Add(0x01, new Callback(0x01, this.SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, this.SetCursorPosition));
        _dispatchTable.Add(0x06, new Callback(0x06, this.ScrollPageUp));
        _dispatchTable.Add(0x0B, new Callback(0x0B, this.SetColorPalette));
        _dispatchTable.Add(0x0E, new Callback(0x0E, this.WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, this.GetVideoStatus));
        _dispatchTable.Add(0x10, new Callback(0x10, this.GetSetPaletteRegisters));
        _dispatchTable.Add(0x12, new Callback(0x12, this.VideoSubsystemConfiguration));
        _dispatchTable.Add(0x1A, new Callback(0x1A, this.VideoDisplayCombination));
    }

    private void VideoDisplayCombination() {
        byte op = _state.AL;
        switch (op) {
            case 0:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("GET VIDEO DISPLAY COMBINATION");
                }
                // VGA with analog color display
                _state.BX = 0x08;
                break;
            case 1:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("SET VIDEO DISPLAY COMBINATION");
                }
                throw new UnhandledOperationException(_machine, "Unimplemented");
            default:
                throw new UnhandledOperationException(_machine,
                    $"Unhandled operation for videoDisplayCombination op={ConvertUtils.ToHex8(op)}");
        }
        _state.AL = 0x1A;
    }

    private void VideoSubsystemConfiguration() {
        byte op = _state.BL;
        switch (op) {
            case 0x0:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("UNKNOWN!");
                }
                break;
            case 0x10:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("GET VIDEO CONFIGURATION INFORMATION");
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