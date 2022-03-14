namespace Spice86.Emulator.Devices.Video;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;
using Spice86.UI;
using Spice86.UI.ViewModels;

/// <summary>
/// Implementation of VGA card, currently only supports mode 0x13.<br/>
/// </summary>
public class VgaCard : DefaultIOPortHandler {
    private static readonly ILogger _logger = Program.Logger.ForContext<VgaCard>();
   
    public const ushort CRT_IO_PORT = 0x03D4;
    // http://www.osdever.net/FreeVGA/vga/extreg.htm#3xAR
    public const ushort VGA_SEQUENCER_ADDRESS_REGISTER_PORT = 0x03C4;
    public const ushort VGA_SEQUENCER_DATA_REGISTER_PORT = 0x03C5;
    public const ushort VGA_READ_INDEX_PORT = 0x03C7;
    public const ushort VGA_WRITE_INDEX_PORT = 0x03C8;
    public const ushort VGA_RGB_DATA_PORT = 0x3C9;
    public const ushort GRAPHICS_ADDRESS_REGISTER_PORT = 0x3CE;
    public const ushort VGA_STATUS_REGISTER_PORT = 0x03DA;

    public const byte MODE_320_200_256 = 0x13;

    private readonly MainWindowViewModel? _gui;
    private byte _crtStatusRegister;
    private bool _drawing = false;

    public VgaCard(Machine machine, MainWindowViewModel? gui, Configuration configuration) : base(machine, configuration) {
        this._gui = gui;
        VgaDac = new VgaDac(machine);
    }

    public void GetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, uint colorValuesAddress) {
        Rgb[] rgbs = VgaDac.Rgbs;
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            _memory.SetUint8(colorValuesAddress++, Video.VgaDac.From8bitTo6bitColor(rgb.R));
            _memory.SetUint8(colorValuesAddress++, Video.VgaDac.From8bitTo6bitColor(rgb.G));
            _memory.SetUint8(colorValuesAddress++, Video.VgaDac.From8bitTo6bitColor(rgb.B));
        }
    }

    public byte GetStatusRegisterPort() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("CHECKING RETRACE");
        }
        TickRetrace();
        return _crtStatusRegister;
    }

    public VgaDac VgaDac { get; private set; }

    public byte GetVgaReadIndex() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET VGA READ INDEX");
        }
        return VgaDac.State == Video.VgaDac.VgaDacWrite ? (byte)0x3 : (byte)0x0;
    }

    public override byte ReadByte(int port) {
        if (port == VGA_READ_INDEX_PORT) {
            return GetVgaReadIndex();
        } else if (port == VGA_STATUS_REGISTER_PORT) {
            return GetStatusRegisterPort();
        } else if (port == VGA_RGB_DATA_PORT) {
            return RgbDataRead();
        }

        return base.ReadByte(port);
    }

    public override void WriteByte(int port, byte value) {
        if (port == VGA_READ_INDEX_PORT) {
            SetVgaReadIndex(value);
        } else if (port == VGA_WRITE_INDEX_PORT) {
            SetVgaWriteIndex(value);
        } else if (port == VGA_RGB_DATA_PORT) {
            RgbDataWrite(value);
        } else if (port == VGA_STATUS_REGISTER_PORT) {
            bool vsync = (value & 0b100) != 1;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Vsync value set to {@VSync} (this is not implemented)", vsync);
            }
        } else {
            base.WriteByte(port, value);
        }
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(VGA_SEQUENCER_ADDRESS_REGISTER_PORT, this);
        ioPortDispatcher.AddIOPortHandler(VGA_SEQUENCER_DATA_REGISTER_PORT, this);
        ioPortDispatcher.AddIOPortHandler(VGA_READ_INDEX_PORT, this);
        ioPortDispatcher.AddIOPortHandler(VGA_WRITE_INDEX_PORT, this);
        ioPortDispatcher.AddIOPortHandler(VGA_RGB_DATA_PORT, this);
        ioPortDispatcher.AddIOPortHandler(GRAPHICS_ADDRESS_REGISTER_PORT, this);
        ioPortDispatcher.AddIOPortHandler(VGA_STATUS_REGISTER_PORT, this);
    }

    public byte RgbDataRead() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PALETTE READ");
        }
        return Video.VgaDac.From8bitTo6bitColor(VgaDac.ReadColor());
    }

    public void RgbDataWrite(byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PALETTE WRITE {@Value}", value);
        }
        VgaDac.WriteColor(Video.VgaDac.From6bitColorTo8bit(value));
    }

    public void SetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, uint colorValuesAddress) {
        Rgb[] rgbs = VgaDac.Rgbs;
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            byte r = Video.VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            byte g = Video.VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            byte b = Video.VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            rgb.R = r;
            rgb.G = g;
            rgb.B = b;
        }
    }

    public void SetVgaReadIndex(int value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET VGA READ INDEX {@Value}", value);
        }
        VgaDac.ReadIndex = value;
        VgaDac.Colour = 0;
        VgaDac.State = Video.VgaDac.VgaDacRead;
    }

    public void SetVgaWriteIndex(int value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET VGA WRITE INDEX {@Value}", value);
        }
        VgaDac.WriteIndex = value;
        VgaDac.Colour = 0;
        VgaDac.State = Video.VgaDac.VgaDacWrite;
    }

    public void SetVideoModeValue(byte mode) {
        if (mode == MODE_320_200_256) {
            int videoHeight = 200;
            int videoWidth = 320;
            if (_gui != null) {
                _gui.SetResolution(videoWidth, videoHeight, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
            }
        } else {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error("UNSUPPORTED VIDEO MODE {@VideMode}", mode);
            }
        }
    }

    /// <returns>true when in retrace</returns>
    public bool TickRetrace() {
        if (_drawing) {
            //_logger.Information("CHECKING RETRACE: Updating screen");
            // Means the CRT is busy drawing a line, tells the program it should not draw
            UpdateScreen();
            _crtStatusRegister = 0;
            _drawing = false;
        } else {
            //_logger.Information("CHECKING RETRACE: Not updating screen");
            // 4th bit is 1 when the CRT finished drawing and is returning to the beginning
            // of the screen (retrace).
            // Programs use this to know if it is safe to write to VRAM.
            // They write to VRAM when this bit is set, but only after waiting for a 0
            // first.
            // This is to be sure to catch the start of the retrace to ensure having the
            // whole duration of the retrace to write to VRAM.
            // More info here: http://atrevida.comprenica.com/atrtut10.html
            _drawing = true;
            _crtStatusRegister = 0b1000;
        }
        return _drawing;
    }

    public void UpdateScreen() {
        if (_gui != null) {
            _gui.Draw(_memory.Ram, VgaDac.Rgbs);
        }
    }
}