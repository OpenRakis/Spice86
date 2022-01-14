namespace Spice86.Emulator.Devices.Video;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;
using Spice86.Gui;

/// <summary>
/// Implementation of VGA card, currently only supports mode 0x13.<br/>
/// </summary>
public class VgaCard : DefaultIOPortHandler {
    public const int CRT_IO_PORT = 0x03D4;
    public const int GRAPHICS_ADDRESS_REGISTER_PORT = 0x3CE;
    public const int MODE_320_200_256 = 0x13;
    public const int VGA_READ_INDEX_PORT = 0x03C7;
    public const int VGA_RGB_DATA_PORT = 0x3C9;

    // http://www.osdever.net/FreeVGA/vga/extreg.htm#3xAR
    public const int VGA_SEQUENCER_ADDRESS_REGISTER_PORT = 0x03C4;

    public const int VGA_SEQUENCER_DATA_REGISTER_PORT = 0x03C5;
    public const int VGA_STATUS_REGISTER_PORT = 0x03DA;
    public const int VGA_WRITE_INDEX_PORT = 0x03C8;
    private static readonly ILogger _logger = Log.Logger.ForContext<VgaCard>();
    private readonly Gui? _gui;
    private readonly VgaDac _vgaDac;
    private byte _crtStatusRegister;
    private bool _drawing = false;

    public VgaCard(Machine machine, Gui gui, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
        this._gui = gui;
        this._vgaDac = new VgaDac(machine);
    }

    public void GetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, int colorValuesAddress) {
        Rgb[] rgbs = _vgaDac.GetRgbs();
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            memory.SetUint8(colorValuesAddress++, (byte)VgaDac.From8bitTo6bitColor(rgb.GetR()));
            memory.SetUint8(colorValuesAddress++, (byte)VgaDac.From8bitTo6bitColor(rgb.GetG()));
            memory.SetUint8(colorValuesAddress++, (byte)VgaDac.From8bitTo6bitColor(rgb.GetB()));
        }
    }

    public int GetStatusRegisterPort() {
        _logger.Information("CHECKING RETRACE");
        TickRetrace();
        return _crtStatusRegister;
    }

    public VgaDac GetVgaDac() {
        return _vgaDac;
    }

    /**
   * @return true when in retrace
   */

    public int GetVgaReadIndex() {
        _logger.Information("GET VGA READ INDEX");
        return _vgaDac.GetState() == VgaDac.VgaDacWrite ? 0x3 : 0x0;
    }

    public override int Inb(int port) {
        if (port == VGA_READ_INDEX_PORT) {
            return GetVgaReadIndex();
        } else if (port == VGA_STATUS_REGISTER_PORT) {
            return GetStatusRegisterPort();
        } else if (port == VGA_RGB_DATA_PORT) {
            return RgbDataRead();
        }

        return base.Inb(port);
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

    public int RgbDataRead() {
        _logger.Information("PALETTE READ");
        return VgaDac.From8bitTo6bitColor(_vgaDac.ReadColor());
    }

    public void RgbDataWrite(int value) {
        _logger.Information("PALETTE WRITE {@Value}", value);
        _vgaDac.WriteColor(VgaDac.From6bitColorTo8bit(value));
    }

    public void SetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, int colorValuesAddress) {
        Rgb[] rgbs = _vgaDac.GetRgbs();
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            rgb.SetR(VgaDac.From6bitColorTo8bit(memory.GetUint8(colorValuesAddress++)));
            rgb.SetG(VgaDac.From6bitColorTo8bit(memory.GetUint8(colorValuesAddress++)));
            rgb.SetB(VgaDac.From6bitColorTo8bit(memory.GetUint8(colorValuesAddress++)));
        }
    }

    public void SetVgaReadIndex(int value) {
        _logger.Information("SET VGA READ INDEX {@Value}", value);
        _vgaDac.SetReadIndex(value);
        _vgaDac.SetColour(0);
        _vgaDac.SetState(VgaDac.VgaDacRead);
    }

    public void SetVgaWriteIndex(int value) {
        _logger.Information("SET VGA WRITE INDEX {@Value}", value);
        _vgaDac.SetWriteIndex(value);
        _vgaDac.SetColour(0);
        _vgaDac.SetState(VgaDac.VgaDacWrite);
    }

    public void SetVideoModeValue(int mode) {
        if (mode == MODE_320_200_256) {
            int videoHeight = 200;
            int videoWidth = 320;
            if (_gui != null) {
                _gui.SetResolution(videoWidth, videoHeight, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
            }
        } else {
            _logger.Error("UNSUPPORTED VIDEO MODE {@VideMode}", mode);
        }
    }

    public bool TickRetrace() {
        if (_drawing) {
            // Means the CRT is busy drawing a line, tells the program it should not draw
            UpdateScreen();
            _crtStatusRegister = 0;
            _drawing = false;
        } else {
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
            _gui.Draw(memory.GetRam(), _vgaDac.GetRgbs());
        }
    }
}