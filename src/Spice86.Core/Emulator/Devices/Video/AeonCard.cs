/*
 * VGA card from Aeon project (https://github.com/gregdivis/Aeon) ported to Spice86.
 */

namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Aeon.Emulator.Video;
using Spice86.Aeon.Emulator.Video.Modes;
using Spice86.Aeon.Emulator.Video.Rendering;

using Serilog.Events;

using Spice86.Aeon;
using Spice86.Aeon.Emulator.Video.Registers;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

public class AeonCard : DefaultIOPortHandler, IVideoCard, IAeonVgaCard, IDisposable {

    /// <summary>
    ///     Total number of bytes allocated for video RAM.
    /// </summary>
    public uint TotalVramBytes => 0x40000;

    private readonly Bios _bios;
    private readonly IGui? _gui;
    private bool _attributeDataMode;
    private AttributeControllerRegister _attributeRegister;
    private CrtControllerRegister _crtRegister;
    private byte _crtStatusRegister;
    private GraphicsRegister _graphicsRegister;
    private Presenter? _presenter;
    private const int VerticalTextResolution = 16;
    private bool _disposed;
    private readonly State _state;
    private readonly ILoggerService _logger;
    private Color _dacReadColor = Color.Black;
    private int _dacReadIndex;
    private int _dacWriteIndex;
    private Color _dacWriteColor;
    private bool _internalPaletteAccessDisabled;
    private VideoModeId _previousVideoMode;
    public GeneralRegisters GeneralRegisters => _videoState.GeneralRegisters;
    private readonly VideoState _videoState;
    private IVgaRenderer _renderer;

    public AeonCard(Machine machine, ILoggerService loggerService, IGui? gui, Configuration configuration) :
        base(machine, configuration, loggerService) {
        _logger = loggerService.WithLogLevel(LogEventLevel.Debug);
        _bios = machine.Bios;
        _state = machine.Cpu.State;
        _gui = gui;

        
        // Initialize registers and other state.
        _videoState = new VideoState();
        
        // Initialize video memory
        var memoryDevice = new VideoMemory(0x20000, this, 0xA0000, _videoState);
        _machine.Memory.RegisterMapping(0xA0000, 0x20000, memoryDevice);
        
        _renderer = new Renderer(_videoState, memoryDevice);
        
        unsafe {
            VideoRam = new nint(NativeMemory.AllocZeroed(TotalVramBytes));
        }

        // InitializeStaticFunctionalityTable();
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
            Ports.CrtControllerAddressAltMirror1,
            Ports.CrtControllerAddressAltMirror2,
            Ports.CrtControllerData,
            Ports.CrtControllerDataAlt,
            Ports.CrtControllerDataAltMirror1,
            Ports.CrtControllerDataAltMirror2,
            Ports.DacAddressWriteIndex,
            Ports.DacData,
            Ports.DacPelMask,
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
            Ports.CrtControllerAddressAltMirror1,
            Ports.CrtControllerAddressAltMirror2,
            Ports.CrtControllerData,
            Ports.CrtControllerDataAlt,
            Ports.CrtControllerDataAltMirror1,
            Ports.CrtControllerDataAltMirror2,
            Ports.DacAddressReadIndex,
            Ports.DacAddressWriteIndex,
            Ports.DacData,
            Ports.DacPelMask,
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
        byte value;
        switch (port) {
            case Ports.DacStateRead:
                value = DacRegisters.State;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC State: {Value:X2}", port, value);
                }
                break;
            case Ports.DacAddressWriteIndex:
                value = DacRegisters.IndexRegisterWriteMode;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC Write Index: {Value:X2}", port, value);
                }
                break;
            case Ports.DacData:
                value = DacRegisters.DataRegister;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read DAC Data Register: {Value:X2}", port, value);
                }
                break;
            case Ports.DacPelMask:
                value = DacRegisters.PixelMask;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC Pel Mask: {Value:X2}", port, value);
                }
                break;
            case Ports.GraphicsControllerAddress:
                value = (byte)_graphicsRegister;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read current Graphics register: {Value:X2} {Register}", port, value, _graphicsRegister);
                }
                break;
            case Ports.GraphicsControllerData:
                value = GraphicsControllerRegisters.ReadRegister(_graphicsRegister);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegister, value, _graphicsRegister.Explain(value));
                }
                break;
            case Ports.SequencerAddress:
                value = (byte)SequencerRegisters.SequencerAddress;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read current _sequencerRegister: {Value:X2} {Register}", port, value, SequencerRegisters.SequencerAddress);
                }
                break;
            case Ports.SequencerData:
                value = SequencerRegisters.ReadRegister();
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from Sequencer register {Register}: {Value:X2} {Explained}", port, SequencerRegisters.SequencerAddress, value, SequencerRegisters.Explain(value));
                }
                break;
            case Ports.AttributeAddress:
                value = (byte)_attributeRegister;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read _attributeRegister: {Value:X2} {Register}", port, value, _attributeRegister);
                }
                break;
            case Ports.AttributeData:
                value = AttributeControllerRegisters.ReadRegister(_attributeRegister);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from Attribute register {Register}: {Value:X2}", port, _attributeRegister, value);
                }
                break;
            case Ports.CrtControllerAddress or Ports.CrtControllerAddressAlt or Ports.CrtControllerAddressAltMirror1 or Ports.CrtControllerAddressAltMirror2:
                value = (byte)_crtRegister;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read _crtRegister: {Value:X2} {Register}", port, value, _crtRegister);
                }
                break;
            case Ports.CrtControllerData or Ports.CrtControllerDataAlt or Ports.CrtControllerDataAltMirror1 or Ports.CrtControllerDataAltMirror2:
                value = CrtControllerRegisters.ReadRegister(_crtRegister);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from CRT register {Register}: {Value:X2} {Explained}", port, _crtRegister, value, _crtRegister.Explain(value));
                }
                break;
            case Ports.InputStatus1Read or Ports.InputStatus1ReadAlt:
                _attributeDataMode = false; // Reset the attribute data mode for port 0x03C0 to "Index"
                value = GeneralRegisters.InputStatusRegister1.Value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read byte from port InputStatus1Read: {Value:X2} {Binary}", port, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                // Next time we will be called retrace will be active, and this until the retrace tick
                // Set vsync flag to true
                GeneralRegisters.InputStatusRegister1.VerticalRetrace = true;
                // CrtStatusRegister |= 0b00001001;
                break;
            case Ports.InputStatus0Read:
                value = GeneralRegisters.InputStatusRegister0.Value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read from InputStatus0Read: {Value:X2} {@FullValue}", port, value, value);
                }
                break;
            case Ports.MiscOutputRead:
                value = GeneralRegisters.MiscellaneousOutput.Value;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read MiscOutput: {Value:X2} {@Explained}", port, value, value);
                }
                break;
            default:
                value = base.ReadByte(port);
                break;
        }

        return value;
    }

    public override ushort ReadWord(int port) {
        byte value = ReadByte(port);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Returning byte {Byte} for ReadWord() on port {Port}", value, port);
        }

        return value;
    }

    public override uint ReadDWord(int port) {
        byte value = ReadByte(port);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Returning byte {Byte} for ReadDWord() on port {Port}", value, port);
        }

        return value;
    }

    public override void WriteByte(int port, byte value) {
        if (_bios.VideoMode != (int)_previousVideoMode) {
            var newMode = (VideoModeId)_bios.VideoMode;
            _logger.Debug("Switching from {PreviousVideoMode} to {NewMode}", _previousVideoMode, newMode);
            SetVideoModeInternal(newMode);
            _previousVideoMode = newMode; 
        }
        switch (port) {
            case Ports.DacAddressReadIndex:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to DacAddressReadIndex: {Value}", port, value);
                }
                DacRegisters.IndexRegisterReadMode = value;
                break;

            case Ports.DacAddressWriteIndex:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to DacAddressWriteIndex: {Value}", port, value);
                }
                DacRegisters.IndexRegisterWriteMode = value;
                break;

            case Ports.DacData:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("[{Port:X4}] Write to DacData: {Value:X2}", port, value);
                }
                DacRegisters.DataRegister = value;
                break;
            
            case Ports.DacPelMask:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to DacPelMask: {Value:X2}", port, value);
                }
                DacRegisters.PixelMask = value;
                break;

            case Ports.GraphicsControllerAddress:
                _graphicsRegister = (GraphicsRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to GraphicsControllerAddress: {Value:X2} {Register}", port, value, _graphicsRegister);
                }
                break;

            case Ports.GraphicsControllerData:
                if (_graphicsRegister is GraphicsRegister.ReadMapSelect or GraphicsRegister.BitMask or GraphicsRegister.GraphicsMode) {
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("[{Port:X4}] Write to Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegister, value, _graphicsRegister.Explain(value));
                    }
                } else if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegister, value, _graphicsRegister.Explain(value));
                }
                GraphicsControllerRegisters.Write(_graphicsRegister, value);
                break;

            case Ports.SequencerAddress:
                SequencerRegisters.SequencerAddress = (SequencerRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to SequencerAddress: {Value:X2} {Register}", port, value, SequencerRegisters.SequencerAddress);
                }
                break;

            case Ports.SequencerData:
                bool previousChain4Mode = SequencerRegisters.MemoryModeRegister.Chain4Mode;
                if (SequencerRegisters.SequencerAddress == SequencerRegister.PlaneMask) {
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("[{Port:X4}] Write to Sequencer register {Register}: {Value:X2} {Explained}", port, SequencerRegisters.SequencerAddress, value, SequencerRegisters.Explain(value));
                    }
                } else if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to Sequencer register {Register}: {Value:X2} {Explained}", port, SequencerRegisters.SequencerAddress, value, SequencerRegisters.Explain(value));
                }
                SequencerRegisters.WriteRegister(value);
                if (previousChain4Mode && !SequencerRegisters.MemoryModeRegister.Chain4Mode) {
                    EnterModeX();
                }
                break;

            case Ports.AttributeAddress:
                if (!_attributeDataMode) {
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        if (_internalPaletteAccessDisabled && (value & 1 << 5) != 0) {
                            _logger.Debug("[{Port:X4}] Enabling internal palette access by setting bit 5 of the Attribute Controller Index to 1 with value {Value:X2}", port, value);
                        } else if (!_internalPaletteAccessDisabled && (value & 1 << 5) == 0) {
                            _logger.Debug("[{Port:X4}] Internal palette access disabled by setting bit 5 of the Attribute Controller Index to 0 with value {Value:X2}", port, value);
                        }
                    }
                    _internalPaletteAccessDisabled = (value & 1 << 5) == 0;
                    _attributeRegister = (AttributeControllerRegister)(value & 0b11111);
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("[{Port:X4}] Write to AttributeAddress: {Value:X2} {Register}", port, value, _attributeRegister);
                    }
                } else {
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        _logger.Debug("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                    }
                    AttributeControllerRegisters.WriteRegister(_attributeRegister, value);
                }

                _attributeDataMode = !_attributeDataMode;
                break;

            case Ports.AttributeData:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                AttributeControllerRegisters.WriteRegister(_attributeRegister, value);
                break;

            case Ports.CrtControllerAddress or Ports.CrtControllerAddressAlt or Ports.CrtControllerAddressAltMirror1 or Ports.CrtControllerAddressAltMirror2:
                _crtRegister = (CrtControllerRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to CrtControllerAddress: {Value:X2} {Register}", port, value, _crtRegister);
                }
                break;

            case Ports.CrtControllerData or Ports.CrtControllerDataAlt or Ports.CrtControllerDataAltMirror1 or Ports.CrtControllerDataAltMirror2:
                int previousVerticalEnd = CrtControllerRegisters.VerticalDisplayEnd;
                int previousMaximumScanLine = CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to CRT register {Register}: {Value:X2} {Explained}", port, _crtRegister, value, _crtRegister.Explain(value));
                }
                CrtControllerRegisters.WriteRegister(_crtRegister, value);
                if (previousMaximumScanLine != CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight) {
                    ChangeMaximumScanLine(previousMaximumScanLine);
                }
                if (previousVerticalEnd != CrtControllerRegisters.VerticalDisplayEnd) {
                    ChangeVerticalEnd();
                }

                break;
            case Ports.MiscOutputWrite:
                GeneralRegisters.MiscellaneousOutput.Value = value;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to MiscOutputWrite: {Value:X2} {@Explained}", port, value, GeneralRegisters.MiscellaneousOutput);
                }
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }
    private void ChangeMaximumScanLine(int previousDoubleScan) {
        int height = previousDoubleScan == 0 ? CurrentMode.Height / 2 : CurrentMode.Height * 2;
        ChangeResolution(CurrentMode.Width, height);
    }
    private void ChangeResolution(int width, int height) {
        VideoMode mode = CurrentMode switch {
            Unchained256 => new Unchained256(width, height, this),
            Vga256 => new Vga256(width, height, this),
            CgaMode4 => new CgaMode4(this),
            EgaVga16 => new EgaVga16(width, height, CurrentMode.FontHeight, this),
            TextMode => new TextMode(width, height, CurrentMode.FontHeight, this),
            _ => throw new InvalidOperationException("Unknown video mode")
        };
        SwitchToMode(mode);
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
    public DacRegisters DacRegisters { get; } = new();

    /// <summary>
    ///     Gets the VGA graphics controller.
    /// </summary>
    public GraphicsControllerRegisters GraphicsControllerRegisters => _videoState.GraphicsControllerRegisters;

    /// <summary>
    ///     Gets the VGA sequencer.
    /// </summary>
    public SequencerRegisters SequencerRegisters => _videoState.SequencerRegisters;

    /// <summary>
    ///     Gets the VGA CRT controller.
    /// </summary>
    public CrtControllerRegisters CrtControllerRegisters => _videoState.CrtControllerRegisters;

    /// <summary>
    ///     Gets the current display mode.
    /// </summary>
    public VideoMode CurrentMode { get; private set; } = null!;

    /// <summary>
    ///     Gets the VGA attribute controller.
    /// </summary>
    public AttributeControllerRegisters AttributeControllerRegisters => _videoState.AttributeControllerRegisters;

    /// <summary>
    ///     Gets a pointer to the emulated video RAM.
    /// </summary>
    public nint VideoRam { get; }

    /// <summary>
    ///     Gets the text-mode display instance.
    /// </summary>
    public TextConsole TextConsole { get; }

    public byte GetVramByte(uint address) {
        // throw new NotSupportedException();
        return CurrentMode.GetVramByte(address);
    }
    public void SetVramByte(uint address, byte value) {
        CurrentMode.SetVramByte(address, value);
    }

    public void Render(uint address, object width, object height, nint pixelsAddress) {
        _presenter ??= GetPresenter();
        _presenter.Update(pixelsAddress);
    }

    public void Render(uint address, IntPtr buffer, int size) {
        _renderer.Render(buffer, size);
        
    }

    public void TickRetrace() {
        // Inactive at tick time, but will become active once the code checks for it.
        // Set vsync flag to false.
        // CrtStatusRegister &= 0b11110110;
        GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
    }

    public void UpdateScreen() {
        _gui?.UpdateScreen();
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
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("INT 10: GetFunctionalityInfo {0}", info);
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

    private void SetVideoModeInternal(VideoModeId id) { 
        VideoMode mode = ConvertIdToMode(id);

        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information("Setting video mode to {Mode}", id);
        }

        // mode.InitializeMode(this);
        // Graphics.WriteRegister(GraphicsRegister.ColorDontCare, 0x0F);

        // if (DefaultPaletteLoading) {
        //     Dac.Reset();
        // }

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Video mode changed to {Mode}", mode.GetType().Name);
        }
        
        SwitchToMode(mode);
    }
    private VideoMode ConvertIdToMode(VideoModeId id) {

        VideoMode mode;

        switch (id) {
            case VideoModeId.ColorText40X25X4:
                mode = new TextMode(40, 25, 8, this);
                break;

            case VideoModeId.ColorText80X25X4:
            case VideoModeId.MonochromeText80X25X4:
                mode = new TextMode(80, 25, VerticalTextResolution, this);
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
        return mode;
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

    public Presenter GetPresenter() {
        if (CurrentMode.VideoModeType == VideoModeType.Text) {
            return new TextPresenter(CurrentMode);
        }

        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information("Initializing graphics presenter for mode {@Mode}", CurrentMode);
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

    private void ChangeVerticalEnd() {
        // TODO: Implement or remove
        // throw new NotImplementedException();
    }

    private void EnterModeX() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("ENTER MODE X");
        }
        var mode = new Unchained256(320, 200, this);
        SwitchToMode(mode);
    }
    private void SwitchToMode(VideoMode mode) {
        CurrentMode = mode;
        _presenter = GetPresenter();
        _gui?.SetResolution(CurrentMode.PixelWidth, CurrentMode.PixelHeight, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
    }

}