

namespace Spice86.Core.Emulator.Devices.Video;

using Serilog;
using Spice86.Logging;
using Spice86.Shared.Interfaces;
using Spice86.Shared;
using Spice86.Core.Emulator.Devices.Video.Fonts;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Implementation of VGA card, currently only supports mode 0x13.<br/>
/// </summary>
public class VgaCard : DefaultIOPortHandler {
    private readonly ILoggerService _loggerService;

    // http://www.osdever.net/FreeVGA/vga/extreg.htm#3xAR
    public const ushort VGA_SEQUENCER_ADDRESS_REGISTER_PORT = 0x03C4;
    public const ushort VGA_SEQUENCER_DATA_REGISTER_PORT = 0x03C5;
    public const ushort VGA_READ_INDEX_PORT = 0x03C7;
    public const ushort VGA_WRITE_INDEX_PORT = 0x03C8;
    public const ushort VGA_RGB_DATA_PORT = 0x3C9;
    public const ushort GRAPHICS_ADDRESS_REGISTER_PORT = 0x3CE;
    public const ushort VGA_STATUS_REGISTER_PORT = 0x03DA;

    public const byte MODE_320_200_256 = 0x13;

    
    // Means the CRT is busy drawing a line, tells the program it should not draw
    private const byte StatusRegisterRetraceInactive = 0;
    // 4th bit is 1 when the CRT finished drawing and is returning to the beginning
    // of the screen (retrace).
    // Programs use this to know if it is safe to write to VRAM.
    // They write to VRAM when this bit is set, but only after waiting for a 0
    // first.
    // This is to be sure to catch the start of the retrace to ensure having the
    // whole duration of the retrace to write to VRAM.
    // More info here: http://atrevida.comprenica.com/atrtut10.html
    private const byte StatusRegisterRetraceActive = 0b1000;
    
    private readonly IGui? _gui;
    private byte _crtStatusRegister = StatusRegisterRetraceActive;
    private readonly LazyConcurrentDictionary<FontType, SegmentedAddress> _fonts = new();
    private ushort _nextFontOffset;

    public VgaCard(Machine machine, ILoggerService loggerService, IGui? gui, Configuration configuration) : base(machine, configuration) {
        _loggerService = loggerService;
        _gui = gui;
        VgaDac = new VgaDac(machine);
        machine.Bios.CrtControllerBaseAddress = 0x03D4;
    }

    public void GetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, uint colorValuesAddress) {
        Rgb[] rgbs = VgaDac.Rgbs;
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            _memory.SetUint8(colorValuesAddress++, VgaDac.From8bitTo6bitColor(rgb.R));
            _memory.SetUint8(colorValuesAddress++, VgaDac.From8bitTo6bitColor(rgb.G));
            _memory.SetUint8(colorValuesAddress++, VgaDac.From8bitTo6bitColor(rgb.B));
        }
    }

    public byte GetStatusRegisterPort() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("CHECKING RETRACE");
        }
        byte res = _crtStatusRegister;
        // Next time we will be called retrace will be active, and this until the retrace tick
        _crtStatusRegister = StatusRegisterRetraceActive;
        return res;
    }

    public VgaDac VgaDac { get; }

    /// <summary>
    /// Returns the address in memory where the specified font is stored.
    /// </summary>
    /// <param name="fontType">One of the <see cref="FontType"/>s</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public SegmentedAddress GetFontAddress(FontType fontType) {
        return _fonts.GetOrAdd(fontType, LoadFont);
    }

    public byte GetVgaReadIndex() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("GET VGA READ INDEX");
        }
        return VgaDac.State == VgaDac.VgaDacWrite ? (byte)0x3 : (byte)0x0;
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
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("Vsync value set to {@VSync} (this is not implemented)", vsync);
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
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("PALETTE READ");
        }
        return VgaDac.From8bitTo6bitColor(VgaDac.ReadColor());
    }

    public void RgbDataWrite(byte value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("PALETTE WRITE {@Value}", value);
        }
        VgaDac.WriteColor(VgaDac.From6bitColorTo8bit(value));
    }

    public void SetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, uint colorValuesAddress) {
        Rgb[] rgbs = VgaDac.Rgbs;
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            byte r = VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            byte g = VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            byte b = VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            rgb.R = r;
            rgb.G = g;
            rgb.B = b;
        }
    }

    public void SetVgaReadIndex(int value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET VGA READ INDEX {@Value}", value);
        }
        VgaDac.ReadIndex = value;
        VgaDac.Colour = 0;
        VgaDac.State = VgaDac.VgaDacRead;
    }

    public void SetVgaWriteIndex(int value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET VGA WRITE INDEX {@Value}", value);
        }
        VgaDac.WriteIndex = value;
        VgaDac.Colour = 0;
        VgaDac.State = VgaDac.VgaDacWrite;
    }

    public void SetVideoModeValue(byte mode) {
        if (mode == MODE_320_200_256) {
            const int videoHeight = 200;
            const int videoWidth = 320;
            _gui?.SetResolution(videoWidth, videoHeight, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
        } else {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("UNSUPPORTED VIDEO MODE {@VideMode}", mode);
            }
        }
    }

    public void TickRetrace() {
        // Inactive at tick time, but will become active once the code checks for it.
        _crtStatusRegister = StatusRegisterRetraceInactive;
    }

    public void UpdateScreen() => _gui?.Draw(_memory.Ram, VgaDac.Rgbs);

    private SegmentedAddress LoadFont(FontType type)
    {
        byte[] bytes = type switch {
            FontType.Ega8X14 => Font.Ega8X14,
            FontType.Ibm8X8 => Font.Ibm8X8,
            FontType.Vga8X16 => Font.Vga8X16,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown font")
        };
        int length = bytes.Length;
        var address = new SegmentedAddress(MemoryMap.VideoBiosSegment, _nextFontOffset);
        // Not using LoadData to avoid triggering breakpoints.
        Array.Copy(bytes, 0, _memory.Ram, address.ToPhysical(), length);
        _nextFontOffset += (ushort)length;

        return address;
    }
}