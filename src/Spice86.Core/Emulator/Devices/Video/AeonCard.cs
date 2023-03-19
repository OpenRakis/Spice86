/*
 * VGA card from Aeon project (https://github.com/gregdivis/Aeon) ported to Spice86.
 */

namespace Spice86.Core.Emulator.Devices.Video;

using Serilog;
using Serilog.Core;
using Serilog.Enrichers;

using Spice86.Aeon.Emulator;
using Spice86.Aeon.Emulator.Video;
using Spice86.Aeon.Emulator.Video.Modes;
using Spice86.Aeon.Emulator.Video.Rendering;

using Serilog.Events;
using Serilog.Exceptions;

using Spice86.Aeon;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video.Fonts;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared;
using Spice86.Shared.Interfaces;

using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

using Point = Spice86.Aeon.Emulator.Video.Point;

public class AeonCard : DefaultIOPortHandler, IVideoCard, IAeonVgaCard, IDisposable, IVgaInterrupts {
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
    private const string LogFormat = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
    /// <summary>
    ///     Total number of bytes allocated for video RAM.
    /// </summary>
    public uint TotalVramBytes => 256 * 1024;

    private readonly Bios _bios;
    private readonly LazyConcurrentDictionary<FontType, SegmentedAddress> _fonts = new();
    private readonly IGui? _gui;
    private readonly ILoggerService _loggerService;
    private bool _attributeDataMode;
    private AttributeControllerRegister _attributeRegister;
    private CrtControllerRegister _crtRegister;
    private byte _crtStatusRegister = StatusRegisterRetraceActive;
    private GraphicsRegister _graphicsRegister;
    private ushort _nextFontOffset;
    private Presenter? _presenter;
    private SequencerRegister _sequencerRegister;
    private int _verticalTextResolution = 16;
    private bool _disposed;
    private readonly State _state;
    private readonly Logger _logger;
    private Color _dacReadColor = Color.Black;
    private int _dacReadIndex;
    private int _dacWriteIndex;
    private Color _dacWriteColor;

    public AeonCard(Machine machine, ILoggerService loggerService, IGui? gui, Configuration configuration) :
        base(machine, configuration) {
        _bios = machine.Bios;
        _state = machine.Cpu.State;
        _loggerService = loggerService;
        _logger = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: LogFormat)
            .WriteTo.File("aeon.log", outputTemplate: LogFormat)
            .MinimumLevel.Warning()
            .CreateLogger();
        _gui = gui;

        unsafe {
            VideoRam = new nint(NativeMemory.AllocZeroed(TotalVramBytes));
        }

        var videoRam = new VideoMemory(256, this, 0xA0000);
        machine.MainMemory.RegisterMapping(0xA0000, 0x10000, videoRam);

        InitializeStaticFunctionalityTable();
        TextConsole = new TextConsole(this, _bios.ScreenColumns, _bios.ScreenRows);
        SetVideoModeInternal(VideoModeId.ColorText80X25X4);

        _presenter = GetPresenter();
    }

    public bool DefaultPaletteLoading { get; set; } = true;

    private static IEnumerable<int> InputPorts =>
        new SortedSet<int> {
            Ports.AttributeAddress,
            Ports.AttributeData,
            Ports.CrtControllerAddress,
            Ports.CrtControllerAddressAlt,
            Ports.CrtControllerData,
            Ports.CrtControllerDataAlt,
            Ports.DacAddressReadMode,
            Ports.DacAddressWriteMode,
            Ports.DacData,
            Ports.DacStateRead,
            Ports.FeatureControlRead,
            Ports.GraphicsControllerAddress,
            Ports.GraphicsControllerData,
            Ports.InputStatus0Read,
            Ports.InputStatus1Read,
            Ports.InputStatus1ReadAlt,
            Ports.MiscOutputRead,
            Ports.SequencerAddress,
            Ports.SequencerData
        };

    private static IEnumerable<int> OutputPorts =>
        new SortedSet<int> {
            Ports.AttributeAddress,
            Ports.AttributeData,
            Ports.CrtControllerAddress,
            Ports.CrtControllerAddressAlt,
            Ports.CrtControllerData,
            Ports.CrtControllerDataAlt,
            Ports.DacAddressReadMode,
            Ports.DacAddressWriteMode,
            Ports.DacData,
            Ports.FeatureControlWrite,
            Ports.FeatureControlWriteAlt,
            Ports.GraphicsControllerAddress,
            Ports.GraphicsControllerData,
            Ports.MiscOutputWrite,
            Ports.SequencerAddress,
            Ports.SequencerData
        };

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        foreach (int port in InputPorts.Union(OutputPorts)) {
            ioPortDispatcher.AddIOPortHandler(port, this);
        }
    }

    public override byte ReadByte(int port) {
        byte value = 0;
        switch (port) {
            case Ports.DacAddressReadMode:
                value = Dac.ReadIndex;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC Read Index: {Value:X2}", port, value);
                }
                break;
            case Ports.DacAddressWriteMode:
                value = Dac.WriteIndex;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC Write Index: {Value:X2}", port, value);
                }
                break;
            case Ports.DacData:
                value = Dac.Read();
                switch (_dacReadIndex) {
                    case 0:
                        _dacReadColor = Color.FromArgb(0xFF, value, _dacReadColor.G, _dacReadColor.B);
                        break;
                    case 1:
                        _dacReadColor = Color.FromArgb(0xFF, _dacReadColor.R, value, _dacReadColor.B);
                        break;
                    case 2:
                        _dacReadColor = Color.FromArgb(0xFF, _dacReadColor.R, _dacReadColor.G, value);
                        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                            _logger.Verbose("[{Port:X4}] Read DAC: {Color}", port, _dacReadColor);
                        }
                        break;
                }
                _dacReadIndex = (_dacReadIndex + 1) % 3;
                break;
            case Ports.GraphicsControllerAddress:
                value = (byte)_graphicsRegister;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read current Graphics register: {Value:X2} {Register}", port, value, _graphicsRegister);
                }
                break;
            case Ports.GraphicsControllerData:
                value = Graphics.ReadRegister(_graphicsRegister);
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read from Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegister, value, _graphicsRegister.Explain(value));
                }
                break;
            case Ports.SequencerAddress:
                value = (byte)_sequencerRegister;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read current _sequencerRegister: {Value:X2} {Register}", port, value, _sequencerRegister);
                }
                break;
            case Ports.SequencerData:
                value = Sequencer.ReadRegister(_sequencerRegister);
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read from Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegister, value, _sequencerRegister.Explain(value));
                }
                break;
            case Ports.AttributeAddress:
                value = (byte)_attributeRegister;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read _attributeRegister: {Value:X2} {Register}", port, value, _attributeRegister);
                }
                break;
            case Ports.AttributeData:
                value = AttributeController.ReadRegister(_attributeRegister);
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read from Attribute register {Register}: {Value:X2}", port, _attributeRegister, value);
                }
                break;
            case Ports.CrtControllerAddress or Ports.CrtControllerAddressAlt:
                value = (byte)_crtRegister;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read _crtRegister: {Value:X2} {Register}", port, value, _crtRegister);
                }
                break;
            case Ports.CrtControllerData or Ports.CrtControllerDataAlt:
                value = CrtController.ReadRegister(_crtRegister);
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read from CRT register {Register}: {Value:X2} {Explained}", port, _crtRegister, value, _crtRegister.Explain(value));
                }
                break;
            case Ports.InputStatus1Read or Ports.InputStatus1ReadAlt:
                _attributeDataMode = false;
                value = GetInputStatus1Value();
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read byte from port InputStatus1Read: {Value:X2} {Binary}", port, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                // Next time we will be called retrace will be active, and this until the retrace tick
                CrtStatusRegister = StatusRegisterRetraceActive;
                break;
            default:
                value = base.ReadByte(port);
                break;
        }

        return value;
    }

    public override ushort ReadWord(int port) {
        byte value = ReadByte(port);

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Returning byte {Byte} for ReadWord() on port {Port}", value, port);
        }

        return value;
    }

    public override uint ReadDWord(int port) {
        byte value = ReadByte(port);

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Returning byte {Byte} for ReadDWord() on port {Port}", value, port);
        }

        return value;
    }

    public override void WriteByte(int port, byte value) {
        switch (port) {
            case Ports.DacAddressReadMode:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to DacAddressReadMode: {Value:X2}", port, value);
                }
                Dac.ReadIndex = value;
                break;

            case Ports.DacAddressWriteMode:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to DacAddressWriteMode: {Value:X2}", port, value);
                }
                Dac.WriteIndex = value;
                break;

            case Ports.DacData:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("[{Port:X4}] Write to DacData: {Value:X2}", port, value);
                }
                Dac.Write(value);
                switch (_dacWriteIndex) {
                    case 0:
                        _dacWriteColor = Color.FromArgb(0xFF, value, _dacWriteColor.G, _dacWriteColor.B);
                        break;
                    case 1:
                        _dacWriteColor = Color.FromArgb(0xFF, _dacWriteColor.R, value, _dacWriteColor.B);
                        break;
                    case 2:
                        _dacWriteColor = Color.FromArgb(0xFF, _dacWriteColor.R, _dacWriteColor.G, value);
                        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                            _logger.Verbose("[{Port:X4}] Write DAC[{Index}]: #{Color:X6}", port, Dac.WriteIndex - 1, _dacWriteColor.ToArgb() & 0x00FFFFFF);
                        }
                        break;
                }
                _dacWriteIndex = (_dacWriteIndex + 1) % 3;
                break;

            case Ports.GraphicsControllerAddress:
                _graphicsRegister = (GraphicsRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to GraphicsControllerAddress: {Value:X2} {Register}", port, value, _graphicsRegister);
                }
                break;

            case Ports.GraphicsControllerData:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegister, value,  _graphicsRegister.Explain(value));
                }
                Graphics.WriteRegister(_graphicsRegister, value);
                break;

            case Ports.SequencerAddress:
                _sequencerRegister = (SequencerRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to SequencerAddress: {Value:X2} {Register}", port, value, _sequencerRegister);
                }
                break;

            case Ports.SequencerData:
                SequencerMemoryMode previousMode = Sequencer.SequencerMemoryMode;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegister, value, _sequencerRegister.Explain(value));
                }
                Sequencer.WriteRegister(_sequencerRegister, value);
                if ((previousMode & SequencerMemoryMode.Chain4) == SequencerMemoryMode.Chain4 &&
                    (Sequencer.SequencerMemoryMode & SequencerMemoryMode.Chain4) == 0) {
                    EnterModeX();
                }
                break;

            case Ports.AttributeAddress:
                if (!_attributeDataMode) {
                    _attributeRegister = (AttributeControllerRegister)(value & 0x1F);
                } else {
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                    }
                    AttributeController.WriteRegister(_attributeRegister, value);
                }

                _attributeDataMode = !_attributeDataMode;
                break;

            case Ports.AttributeData:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                AttributeController.WriteRegister(_attributeRegister, value);
                break;

            case Ports.CrtControllerAddress:
            case Ports.CrtControllerAddressAlt:
                _crtRegister = (CrtControllerRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to CrtControllerAddress: {Value:X2} {Register}", port, value, _crtRegister);
                }
                break;

            case Ports.CrtControllerData:
            case Ports.CrtControllerDataAlt:
                int previousVerticalEnd = CrtController.VerticalDisplayEnd;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to CRT register {Register}: {Value:X2} {Explained}", port, _crtRegister, value, _crtRegister.Explain(value));
                }
                CrtController.WriteRegister(_crtRegister, value);
                if (previousVerticalEnd != CrtController.VerticalDisplayEnd) {
                    ChangeVerticalEnd();
                }

                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <summary>
    /// Special shortcut for VGA controller to select a register and write a value in one call.
    /// </summary>
    public override void WriteWord(int port, ushort value) {
        _machine.IoPortDispatcher.WriteByte(port, (byte)(value & 0xFF));
        _machine.IoPortDispatcher.WriteByte(port + 1, (byte)(value >> 8));
    }

    /// <summary>
    ///     Gets the VGA DAC.
    /// </summary>
    public Dac Dac { get; } = new();

    /// <summary>
    ///     Gets the VGA graphics controller.
    /// </summary>
    public Graphics Graphics { get; } = new();

    /// <summary>
    ///     Gets the VGA sequencer.
    /// </summary>
    public Sequencer Sequencer { get; } = new();

    /// <summary>
    ///     Gets the VGA CRT controller.
    /// </summary>
    public CrtController CrtController { get; } = new();

    /// <summary>
    ///     Gets the current display mode.
    /// </summary>
    public VideoMode CurrentMode { get; private set; } = null!;

    /// <summary>
    ///     Gets the VGA attribute controller.
    /// </summary>
    public AttributeController AttributeController { get; } = new();

    /// <summary>
    ///     Gets a pointer to the emulated video RAM.
    /// </summary>
    public nint VideoRam { get; }

    /// <summary>
    ///     Gets the text-mode display instance.
    /// </summary>
    public TextConsole TextConsole { get; }
    public byte CrtStatusRegister {
        get => _crtStatusRegister;
        set {
            if (_crtStatusRegister != value) {
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("Setting _crtStatusRegister to {Value:X2}", value);
                }
                _crtStatusRegister = value;
            }
        }
    }

    public byte GetVramByte(uint address) {
        return CurrentMode.GetVramByte(address);
    }
    public ushort GetVramWord(uint address) {
        return CurrentMode.GetVramWord(address);
    }
    public uint GetVramDWord(uint address) {
        return CurrentMode.GetVramDWord(address);
    }
    public void SetVramByte(uint address, byte value) {
        CurrentMode.SetVramByte(address, value);
    }
    public void SetVramWord(uint address, ushort value) {
        CurrentMode.SetVramWord(address, value);
    }
    public void SetVramDWord(uint address, uint value) {
        CurrentMode.SetVramDWord(address, value);
    }


    public void Render(uint address, object width, object height, nint pixelsAddress) {
        _presenter ??= GetPresenter();
        _presenter.Update(pixelsAddress);
    }

    public void TickRetrace() {
        // Inactive at tick time, but will become active once the code checks for it.
        CrtStatusRegister = StatusRegisterRetraceInactive;
    }

    public void UpdateScreen() {
        _gui?.UpdateScreen();
    }

    public event EventHandler? VideoModeChanged;

    public void WriteString() {
        if (_logger.IsEnabled(LogEventLevel.Information)) {
            uint address = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BP);
            string str = MemoryUtils.GetZeroTerminatedString(_memory.Ram, address, _memory.Ram.Length - (int)address);
            _logger.Information("WRITE STRING: {0}", str);
        }
    }

    public void SetVideoMode() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("INT 10: Set video mode to {0:X2}", _state.AL);
        }
        SetVideoModeInternal((VideoModeId)_state.AL);
        _bios.VideoMode = _state.AL;
    }

    public VideoFunctionalityInfo GetFunctionalityInfo() {
        ushort segment = _state.ES;
        ushort offset = _state.DI;

        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
        var info = new VideoFunctionalityInfo(_memory, address) {
            SftAddress = MemoryMap.StaticFunctionalityTableSegment << 16,
            VideoMode = _bios.VideoMode,
            ScreenColumns = _bios.ScreenColumns,
            VideoBufferLength = MemoryMap.VideoBiosSegment - MemoryMap.GraphicVideoMemorySegment, // TODO: real value
            VideoBufferAddress = MemoryMap.GraphicVideoMemorySegment, // TODO: real value
            CursorEndLine = 0, // TODO: figure out what this is
            CursorStartLine = 0, // TODO: figure out what this is
            ActiveDisplayPage = (byte)CurrentMode.ActiveDisplayPage,
            CrtControllerBaseAddress = _bios.CrtControllerBaseAddress,
            CurrentRegister3X8Value = 0, // Unused in VGA
            CurrentRegister3X9Value = 0, // Unused in VGA
            ScreenRows = _bios.ScreenRows,
            CharacterMatrixHeight = (ushort)CurrentMode.FontHeight,
            ActiveDisplayCombinationCode = _bios.DisplayCombinationCode,
            AlternateDisplayCombinationCode = 0x00, // No secondary display
            NumberOfColorsSupported = (ushort)(1 << CurrentMode.BitsPerPixel),
            NumberOfPages = 4,
            NumberOfActiveScanLines = 0, // TODO: figure out what this is
            TextCharacterTableUsed = 0, // TODO: figure out what this is
            TextCharacterTableUsed2 = 0, // TODO: figure out what this is
            OtherStateInformation = 0b00000001,
            VideoRamAvailable = 3, // 0=64K, 1=128K, 2=192K, 3=256K
            SaveAreaStatus = 0b00000000
        };
        for (int i = 0; i < 8; i++) {
            info.SetCursorPosition(i, (byte)TextConsole.CursorPosition.X, (byte)TextConsole.CursorPosition.Y);
        }

        // Indicate success.
        _state.AL = 0x1B;
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("INT 10: GetFunctionalityInfo {0}", info);
        }
        return info;
    }

    /// <summary>
    /// Writes values to the static functionality table in emulated memory.
    /// </summary>
    private void InitializeStaticFunctionalityTable() {
        _memory.UInt32[MemoryMap.StaticFunctionalityTableSegment, 0] = 0x000FFFFF; // supports all video modes
        _memory.UInt8[MemoryMap.StaticFunctionalityTableSegment, 0x07] = 0x07; // supports all scanLines
    }

    public void VideoDisplayCombination() {
        if (_state.AL == 0x00) {
            _state.AL = 0x1A; // Function supported
            _state.BL = _bios.DisplayCombinationCode; // Primary display
            _state.BH = 0x00; // No secondary display
            if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                _logger.Verbose("INT 10: Get display combination {0:X2}", _state.BL);
            }
        } else if (_state.AL == 0x01) {
            if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                _logger.Verbose("INT 10: Set display combination {0:X2}", _state.BL);
            }
            _state.AL = 0x1A; // Function supported
            _bios.DisplayCombinationCode = _state.BL;
        }
    }

    public void VideoSubsystemConfiguration() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("INT 10: VideoSubsystemConfiguration {0:X2}", _state.BL);
        }
        switch (_state.BL) {
            case Functions.EGA_GetInfo:
                _state.BX = 0x03; // 256k installed
                _state.CX = 0x09; // EGA switches set
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: VideoSubsystemConfiguration - EGA_GetInfo");
                }
                break;

            case Functions.EGA_SelectVerticalResolution:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: VideoSubsystemConfiguration - EGA_SelectVerticalResolution {0:X2}", _state.AL);
                }
                _verticalTextResolution = _state.AL switch {
                    0 => 8,
                    1 => 14,
                    _ => 16
                };
                _state.AL = 0x12; // Success
                break;

            case Functions.EGA_PaletteLoading:
                DefaultPaletteLoading = _state.AL == 0;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: VideoSubsystemConfiguration - EGA_PaletteLoading {0}", DefaultPaletteLoading);
                }
                break;

            default:
                _logger.Error("Video command {0:X2}, BL={1:X2}h not implemented.", Functions.EGA, _state.BL);
                break;
        }
    }

    public void CharacterGeneratorRoutine() {
        switch (_state.AL) {
            case 0x30:
                GetFontInformation();
                break;

            default:
                throw new NotImplementedException($"Video command 11{_state.AL:X2}h not implemented.");
        }
    }

    /// <summary>
    ///     Returns the address in memory where the specified font is stored.
    /// </summary>
    /// <param name="fontType">One of the <see cref="FontType" />s</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public SegmentedAddress GetFontAddress(FontType fontType) {
        return _fonts.GetOrAdd(fontType, LoadFont);
    }

    private SegmentedAddress LoadFont(FontType type) {
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

    private void GetFontInformation() {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("INT 10: GetFontInformation {0:X2}", _state.BH);
        }
        SegmentedAddress address = _state.BH switch {
            0x00 => new SegmentedAddress(_memory.GetUint16((0x1F * 4) + 2), _memory.GetUint16(0x1F * 4)),
            0x01 => new SegmentedAddress(_memory.GetUint16((0x43 * 4) + 2), _memory.GetUint16(0x43 * 4)),
            0x02 => GetFontAddress(FontType.Ega8X14),
            0x03 => GetFontAddress(FontType.Ibm8X8),
            0x04 => GetFontAddress(FontType.Ibm8X8) + (128 * 8), // 2nd half
            0x05 => throw new NotImplementedException("No 9x14 font available"),
            0x06 => GetFontAddress(FontType.Vga8X16),
            0x07 => throw new NotImplementedException("No 9x16 font available"),
            _ => throw new NotImplementedException($"Video command 1130_{_state.BH:X2}h not implemented.")
        };

        _state.ES = address.Segment;
        _state.BP = address.Offset;
        _state.CX = _machine.Bios.CharacterPointHeight;
        _state.DL = _machine.Bios.ScreenRows;
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("INT 10: GetFontInformation - {0:X4}:{1:X4} {2} {3}", _state.ES, _state.BP, _state.CX, _state.DL);
        }
    }

    public void GetSetPaletteRegisters() {
        switch (_state.AL) {
            case Functions.Palette_SetSingleRegister:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: SetSinglePaletteRegister {0:X2} {1:X2}", _state.BL, _state.BH);
                }
                SetEgaPaletteRegister(_state.BL, _state.BH);
                break;

            case Functions.Palette_SetBorderColor:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: SetBorderColor UNIMPLEMENTED");
                }
                // TODO: Implement, ignore or remove
                break;

            case Functions.Palette_SetAllRegisters:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: SetAllPaletteRegisters");
                }
                SetAllEgaPaletteRegisters();
                break;

            case Functions.Palette_ReadSingleDacRegister:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: ReadSingleDacRegister {0:X2}", _state.BL);
                }
                // TODO: Investigate and fix this.
                // These are commented out because they cause weird issues sometimes.
                //vm.Processor.DH = (byte)((dac.Palette[vm.Processor.BL] >> 18) & 0xCF);
                //vm.Processor.CH = (byte)((dac.Palette[vm.Processor.BL] >> 10) & 0xCF);
                //vm.Processor.CL = (byte)((dac.Palette[vm.Processor.BL] >> 2) & 0xCF);
                break;

            case Functions.Palette_SetSingleDacRegister:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: SetSingleDacRegister {0:X2} {1:X2} {2:X2} {3:X2}", _state.BL, _state.DH, _state.CH, _state.CL);
                }
                Dac.SetColor(_state.BL, _state.DH, _state.CH, _state.CL);
                break;

            case Functions.Palette_SetDacRegisters:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: SetDacRegisters");
                }
                SetDacRegisters();
                break;

            case Functions.Palette_ReadDacRegisters:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: ReadDacRegisters");
                }
                ReadDacRegisters();
                break;

            case Functions.Palette_ToggleBlink:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("INT 10: ToggleBlink UNIMPLEMENTED");
                }
                // TODO: Implement, ignore or remove
                break;

            case Functions.Palette_SelectDacColorPage:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("Select DAC color page UNIMPLEMENTED");
                }

                break;

            default:
                throw new NotImplementedException($"Video command 10 {_state.AL:X2} not implemented.");
        }
    }

    /// <summary>
    ///   Reads DAC color registers to emulated RAM.
    /// </summary>
    private void ReadDacRegisters() {
        ushort segment = _state.ES;
        ushort offset = _state.DX;
        int start = _state.BX;
        int count = _state.CX;

        for (int i = start; i < count; i++) {
            uint r = (Dac.Palette[start + i] >> 18) & 0xCFu;
            uint g = (Dac.Palette[start + i] >> 10) & 0xCFu;
            uint b = (Dac.Palette[start + i] >> 2) & 0xCFu;

            _memory.UInt8[segment, offset++] = (byte)r;
            _memory.UInt8[segment, offset++] = (byte)g;
            _memory.UInt8[segment, offset++] = (byte)b;
        }
    }

    /// <summary>
    ///   Sets DAC color registers to values in emulated RAM.
    /// </summary>
    private void SetDacRegisters() {
        ushort segment = _state.ES;
        ushort offset = _state.DX;
        int start = _state.BX;
        int count = _state.CX;

        for (int i = start; i < count; i++) {
            byte r = _memory.UInt8[segment, offset++];
            byte g = _memory.UInt8[segment, offset++];
            byte b = _memory.UInt8[segment, offset++];

            Dac.SetColor((byte)(start + i), r, g, b);
        }
    }

    /// <summary>
    ///   Sets all of the EGA color palette registers to values in emulated RAM.
    /// </summary>
    private void SetAllEgaPaletteRegisters() {
        ushort segment = _state.ES;
        ushort offset = _state.DX;
        for (int i = 0; i < 16u; i++, offset++) {
            SetEgaPaletteRegister(i, _memory.UInt8[segment, offset]);
        }
    }

    /// <summary>
    ///   Gets a specific EGA color palette register.
    /// </summary>
    /// <param name="index">Index of color to set.</param>
    /// <param name="color">New value of the color.</param>
    private void SetEgaPaletteRegister(int index, byte color) {
        if (_bios.VideoMode == (byte)VideoModeId.ColorGraphics320X200X4) {
            AttributeController.InternalPalette[index & 0x0F] = (byte)(color & 0x0F);
        } else {
            AttributeController.InternalPalette[index & 0x0F] = color;
        }
    }

    public void GetVideoMode() {
        _state.AH = _bios.ScreenColumns;
        _state.AL = _bios.VideoMode;
        _state.BH = _bios.CurrentVideoPage;
    }

    public void WriteTextInTeletypeMode() {
        // TODO: Implement or remove
        throw new NotImplementedException();
    }

    public void SetColorPaletteOrBackGroundColor() {
        // TODO: Implement or remove
        throw new NotImplementedException();
    }

    public void WriteCharacterAtCursor() {
        TextConsole.Write(_state.AL);
    }

    public void WriteCharacterAndAttributeAtCursor() {
        TextConsole.Write(_state.AL, (byte)(_state.BL & 0x0F), (byte)(_state.BL >> 4), false);
    }

    public void ReadCharacterAndAttributeAtCursor() {
        _state.AX = TextConsole.GetCharacter(TextConsole.CursorPosition.X, TextConsole.CursorPosition.Y);
    }

    public void ScrollPageDown() {
        // TODO: Implement or remove
        throw new NotImplementedException();
    }

    public void ScrollPageUp() {
        byte foreground = (byte)((_state.BX >> 8) & 0x0F);
        byte background = (byte)((_state.BX >> 12) & 0x0F);
        TextConsole.ScrollTextUp(_state.CL, _state.CH, _state.DL, _state.DH, _state.AL, foreground, background);
    }

    public void SelectActiveDisplayPage() {
        CurrentMode.ActiveDisplayPage = _state.AL;
    }

    public void GetCursorPosition() {
        _state.CH = 14;
        _state.CL = 15;
        _state.DH = (byte)TextConsole.CursorPosition.Y;
        _state.DL = (byte)TextConsole.CursorPosition.X;
    }

    public void SetCursorPosition() {
        TextConsole.CursorPosition = new Point(_state.DL, _state.DH);
    }

    public void SetCursorType() {
        byte topScanLine = _state.CH;
        byte bottomScanLine = _state.CL;
        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information("SET CURSOR TYPE, SCAN LINE TOP: {@Top} BOTTOM: {@Bottom}", topScanLine,
                bottomScanLine);
        }
    }

    private void SetVideoModeInternal(VideoModeId id) {
        VideoMode mode;

        switch (id) {
            case VideoModeId.ColorText40X25X4:
                mode = new TextMode(40, 25, 8, this);
                break;

            case VideoModeId.ColorText80X25X4:
            case VideoModeId.MonochromeText80X25X4:
                mode = new TextMode(80, 25, _verticalTextResolution, this);
                break;

            case VideoModeId.ColorGraphics320X200X2A:
            case VideoModeId.ColorGraphics320X200X2B:
                mode = new CgaMode4(this);
                break;

            case VideoModeId.ColorGraphics320X200X4:
                mode = new EgaVga16(320, 200, 8, this);
                break;

            case VideoModeId.ColorGraphics640X200X4:
                mode = new EgaVga16(640, 400, 8, this);
                break;

            case VideoModeId.ColorGraphics640X350X4:
                mode = new EgaVga16(640, 350, 8, this);
                break;

            case VideoModeId.Graphics640X480X4:
                mode = new EgaVga16(640, 480, 16, this);
                break;

            case VideoModeId.Graphics320X200X8:
                Sequencer.SequencerMemoryMode = SequencerMemoryMode.Chain4 | SequencerMemoryMode.ExtendedMemory | SequencerMemoryMode.OddEvenWriteAddressingDisabled;
                mode = new Vga256(320, 200, this);
                break;

            case VideoModeId.Text40X25X1:
            case VideoModeId.Graphics640X200X1:
            case VideoModeId.Text80X25X1:
            case VideoModeId.Graphics640X350X1:
            case VideoModeId.Graphics640X480X1:
            default:
                throw new NotSupportedException($"Video mode {id} is not supported.");
        }

        _logger.Information("Setting video mode to {@Mode}", id);

        _gui?.SetResolution(mode.PixelWidth, mode.PixelHeight,
            MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));

        SetDisplayMode(mode);
        _bios.VideoMode = _state.AL;
    }

    ~AeonCard() => InternalDispose();

    void IDisposable.Dispose() {
        InternalDispose();
        GC.SuppressFinalize(this);
    }

    private void InternalDispose() {
        if (!_disposed) {
            unsafe {
                if (VideoRam != IntPtr.Zero)
                    NativeMemory.Free(VideoRam.ToPointer());
            }

            _disposed = true;
        }
    }

    /// <summary>
    ///   Initializes a new display mode.
    /// </summary>
    /// <param name="mode">New display mode.</param>
    public void SetDisplayMode(VideoMode mode) {
        CurrentMode = mode;
        mode.InitializeMode(this);
        Graphics.WriteRegister(GraphicsRegister.ColorDontCare, 0x0F);

        if (DefaultPaletteLoading) {
            Dac.Reset();
        }

        _logger.Information("Video mode changed to {@Mode}", mode.GetType().Name);
        _presenter = GetPresenter();
        VideoModeChanged?.Invoke(this, new VideoModeChangedEventArgs(true));
    }

    public Presenter GetPresenter() {
        if (CurrentMode.VideoModeType == VideoModeType.Text) {
            return new TextPresenter(CurrentMode);
        }

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Initializing graphics presenter for mode {@Mode}", CurrentMode);
        }

        return CurrentMode.BitsPerPixel switch {
            2 => new GraphicsPresenter2(CurrentMode),
            4 => new GraphicsPresenter4(CurrentMode),
            8 when CurrentMode.IsPlanar => new GraphicsPresenterX(CurrentMode),
            8 when !CurrentMode.IsPlanar => new GraphicsPresenter8(CurrentMode),
            16 => new GraphicsPresenter16(CurrentMode),
            _ => throw new InvalidOperationException("Unsupported video mode.")
        };
    }

    /// <summary>
    ///     Returns the current value of the input status 1 register.
    /// </summary>
    /// <returns>Current value of the input status 1 register.</returns>
    private byte GetInputStatus1Value() {
        // uint value = InterruptTimer.IsInRealtimeInterval(VerticalBlankingTime, RefreshRate) ? 0x09u : 0x00u;
        // if (InterruptTimer.IsInRealtimeInterval(HorizontalBlankingTime, HorizontalPeriod))
        //     value |= 0x01u;
        //
        // return (byte)value;
        /*
         * bit 7,6: reserved
         * bit 5,4: "video feedback" color debug bits
         * bit 3: Vertical Retrace/Video (VSYNC)
         * bit 2,1: reserved
         * bit 0: Display Enable
         */
        byte res = CrtStatusRegister;

        return res;
    }

    private void ChangeVerticalEnd() {
        // TODO: Implement or remove
        throw new NotImplementedException();
    }

    private void EnterModeX() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("ENTER MODE X");
        }
        var mode = new Unchained256(320, 200, this);
        CrtController.Offset = 320 / 8;
        CurrentMode = mode;
        _presenter = GetPresenter();
        VideoModeChanged?.Invoke(this, new VideoModeChangedEventArgs(false));
    }

}

public class VideoMemory : Memory {
    private readonly IVideoCard _videoCard;
    private readonly uint _baseAddress;

    public VideoMemory(uint sizeInKb, IVideoCard videoCard, uint baseAddress) : base(sizeInKb) {
        _videoCard = videoCard;
        _baseAddress = baseAddress;
    }

    public override ushort GetUint16(uint address) {
        return _videoCard.GetVramWord(address - _baseAddress);
    }
    public override uint GetUint32(uint address) {
        return _videoCard.GetVramDWord(address - _baseAddress);
    }
    public override byte GetUint8(uint address) {
        return _videoCard.GetVramByte(address - _baseAddress);
    }
    public override void SetUint16(uint address, ushort value) {
        _videoCard.SetVramWord(address - _baseAddress, value);
    }
    public override void SetUint32(uint address, uint value) {
        _videoCard.SetVramDWord(address - _baseAddress, value);
    }
    public override void SetUint8(uint address, byte value) {
        _videoCard.SetVramByte(address - _baseAddress, value);
    }
}