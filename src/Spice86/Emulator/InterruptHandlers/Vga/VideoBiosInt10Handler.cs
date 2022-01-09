namespace Spice86.Emulator.InterruptHandlers.Vga;

using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;
using Spice86.Emulator.Callback;
using Spice86.Emulator.Devices.Video;
using Spice86.Utils;
using Spice86.Emulator.Errors;
using Serilog;
using System;

public class VideoBiosInt10Handler : InterruptHandler
{
    private static readonly ILogger _logger = Log.Logger.ForContext<VideoBiosInt10Handler>();
    public static readonly int CRT_IO_PORT_ADDRESS_IN_RAM = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, MemoryMap.BiosDataAreaOffsetCrtIoPort);
    public const int BiosVideoMode = 0x49;
    public static readonly int BIOS_VIDEO_MODE_ADDRESS = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, BiosVideoMode);
    private readonly VgaCard _vgaCard;
    private readonly int _numberOfScreenColumns = 80;
    private readonly int _currentDisplayPage = 0;
    public VideoBiosInt10Handler(Machine machine, VgaCard vgaCard) : base(machine)
    {
        this._vgaCard = vgaCard;
        FillDispatchTable();
    }

    private void FillDispatchTable()
    {
        _dispatchTable.Add(0x00, new Callback(0x00, () => this.SetVideoMode()));
        _dispatchTable.Add(0x01, new Callback(0x01, () => this.SetCursorType()));
        _dispatchTable.Add(0x02, new Callback(0x02, () => this.SetCursorPosition()));
        _dispatchTable.Add(0x06, new Callback(0x06, () => this.ScrollPageUp()));
        _dispatchTable.Add(0x0B, new Callback(0x0B, () => this.SetColorPalette()));
        _dispatchTable.Add(0x0E, new Callback(0x0E, () => this.WriteTextInTeletypeMode()));
        _dispatchTable.Add(0x0F, new Callback(0x0F, () => this.GetVideoStatus()));
        _dispatchTable.Add(0x10, new Callback(0x10, () => this.GetSetPaletteRegisters()));
        _dispatchTable.Add(0x12, new Callback(0x12, () => this.VideoSubsystemConfiguration()));
        _dispatchTable.Add(0x1A, new Callback(0x1A, () => this.VideoDisplayCombination()));
    }

    private void VideoDisplayCombination()
    {
        int op = _state.GetAL();
        if(op == 0)
        {
            _logger.Information("GET VIDEO DISPLAY COMBINATION");
            // VGA with analog color display
            _state.SetBX(0x08);
        }
        else if(op == 1)
        {
            _logger.Information("SET VIDEO DISPLAY COMBINATION");
            throw new UnhandledOperationException(machine, "Unimplemented");
        }
        else
        {
            throw new UnhandledOperationException(machine,
                $"Unhandled operation for videoDisplayCombination op={ConvertUtils.ToHex8(op)}");
        }
        _state.SetAL(0x1A);
    }

    private void VideoSubsystemConfiguration()
    {
        int op = _state.GetBL();
        if (op == 0x0)
        {
            _logger.Information("UNKNOWN!");
            return;
        }
        if (op == 0x10)
        {
            _logger.Information("GET VIDEO CONFIGURATION INFORMATION");
            // color
            _state.SetBH(0);
            // 64k of vram
            _state.SetBL(0);
            // From dosbox source code ...
            _state.SetCH(0);
            _state.SetCL(0x09);
            return;
        }
        throw new UnhandledOperationException(machine,
        $"Unhandled operation for videoSubsystemConfiguration op={ConvertUtils.ToHex8(op)}");
    }

    public void InitRam()
    {
        this.SetVideoModeValue(VgaCard.MODE_320_200_256);
        memory.SetUint16(CRT_IO_PORT_ADDRESS_IN_RAM, VgaCard.CRT_IO_PORT);
    }

    public override int GetIndex()
    {
        return 0x10;
    }

    public override void Run()
    {
        int operation = _state.GetAH();
        this.Run(operation);
    }

    public void SetVideoMode()
    {
        int videoMode = _state.GetAL();
        SetVideoModeValue(videoMode);
    }

    public void SetCursorType()
    {
        int cursorStartEnd = _state.GetCX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("SET CURSOR TYPE, SCAN LINE START END IS {@CursorStartEnd}", ConvertUtils.ToHex(cursorStartEnd));
        }
    }

    public void SetCursorPosition()
    {
        int cursorPositionRow = _state.GetDH();
        int cursorPositionColumn = _state.GetDL();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("SET CURSOR POSITION, {@Row}, {@Column}", ConvertUtils.ToHex8(cursorPositionRow), ConvertUtils.ToHex8(cursorPositionColumn));
        }
    }

    public void ScrollPageUp()
    {
        int scrollAmount = _state.GetAL();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("SCROLL PAGE UP BY AMOUNT {@ScrollAmount}", ConvertUtils.ToHex8(scrollAmount));
        }
    }

    public void SetColorPalette()
    {
        int colorId = _state.GetBH();
        int colorValue = _state.GetBL();
        _logger.Information("SET COLOR PALETTE {@ColorId}, {@ColorValue}", colorId, colorValue);
    }

    public void WriteTextInTeletypeMode()
    {
        int chr = _state.GetAL();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("Write Text in Teletype Mode ascii code {@AsciiCode}, chr {@Character}", ConvertUtils.ToHex(chr), ConvertUtils.ToChar(chr));
        }
    }

    public void GetVideoStatus()
    {
        _logger.Debug("GET VIDEO STATUS");
        _state.SetAH(_numberOfScreenColumns);
        _state.SetAL(GetVideoModeValue());
        _state.SetBH(_currentDisplayPage);
    }

    public void GetSetPaletteRegisters()
    {
        int op = _state.GetAL();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("GET/SET PALETTE REGISTERS {@Operation}", ConvertUtils.ToHex8(op));
        }

        if (op == 0x12)
        {
            SetBlockOfDacColorRegisters();
        }
        else if (op == 0x17)
        {
            GetBlockOfDacColorRegisters();
        }
        else
        {
            throw new UnhandledOperationException(machine, $"Unhandled operation for get/set palette registers op={ConvertUtils.ToHex8(op)}");
        }
    }

    public void SetBlockOfDacColorRegisters()
    {
        int firstRegisterToSet = _state.GetBX();
        int numberOfColorsToSet = _state.GetCX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("SET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToSet}, setting {@NumberOfColorsToSet} colors, values are from address {@EsDx}", ConvertUtils.ToHex(firstRegisterToSet), numberOfColorsToSet, ConvertUtils.ToSegmentedAddressRepresentation(_state.GetES(), _state.GetDX()));
        }

        int colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.GetES(), _state.GetDX());
        _vgaCard.SetBlockOfDacColorRegisters(firstRegisterToSet, numberOfColorsToSet, colorValuesAddress);
    }

    public void GetBlockOfDacColorRegisters()
    {
        int firstRegisterToGet = _state.GetBX();
        int numberOfColorsToGet = _state.GetCX();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("GET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToGet}, getting {@NumberOfColorsToGet} colors, values are to be stored at address {@EsDx}", ConvertUtils.ToHex(firstRegisterToGet), numberOfColorsToGet, ConvertUtils.ToSegmentedAddressRepresentation(_state.GetES(), _state.GetDX()));
        }

        int colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.GetES(), _state.GetDX());
        _vgaCard.GetBlockOfDacColorRegisters(firstRegisterToGet, numberOfColorsToGet, colorValuesAddress);
    }

    public int GetVideoModeValue()
    {
        _logger.Information("GET VIDEO MODE");
        return memory.GetUint8(BIOS_VIDEO_MODE_ADDRESS);
    }

    public void SetVideoModeValue(int mode)
    {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
        {
            _logger.Information("SET VIDEO MODE {@VideoMode}", ConvertUtils.ToHex8(mode));
        }

        memory.SetUint8(BIOS_VIDEO_MODE_ADDRESS, (byte)mode);
        _vgaCard.SetVideoModeValue(mode);
    }
}
