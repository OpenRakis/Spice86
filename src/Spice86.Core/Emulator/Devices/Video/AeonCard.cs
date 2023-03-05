using Aeon.Emulator;
using Aeon.Emulator.Video;
using Aeon.Emulator.Video.Modes;
using Aeon.Emulator.Video.Rendering;

using Spice86.Core.Emulator.Devices.Video.Fonts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using System.Linq;
using System.Runtime.InteropServices;

namespace Spice86.Core.Emulator.Devices.Video;

public class AeonCard : InterruptHandler, IVideoCard, IVgaCard, IIOPortHandler {
    private GraphicsRegister _graphicsRegister;
    private SequencerRegister _sequencerRegister;
    private AttributeControllerRegister _attributeRegister;
    private CrtControllerRegister _crtRegister;
    private bool _attributeDataMode;
    private readonly Bios _bios;
    private const int VerticalTextResolution = 16;
    private readonly LazyConcurrentDictionary<FontType, SegmentedAddress> _fonts = new();
    private ushort _nextFontOffset;

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
    private byte _crtStatusRegister = StatusRegisterRetraceActive;
    private readonly ILoggerService _logger;
    private readonly IGui? _gui;
    private readonly Configuration _configuration;
    private Presenter? _presenter;

    /// <summary>
    /// Total number of bytes allocated for video RAM.
    /// </summary>
    public const int TotalVramBytes = 1024 * 1024;
    
    /// <summary>
    /// Gets the VGA DAC.
    /// </summary>
    public Dac Dac { get; } = new();

    /// <summary>
    /// Gets the VGA graphics controller.
    /// </summary>
    public Graphics Graphics { get; } = new();

    /// <summary>
    /// Gets the VGA sequencer.
    /// </summary>
    public Sequencer Sequencer { get; } = new();

    /// <summary>
    /// Gets the VGA CRT controller.
    /// </summary>
    public CrtController CrtController { get; } = new();

    /// <summary>
    /// Gets the current display mode.
    /// </summary>
    public VideoMode CurrentMode { get; private set; } = null!;

    /// <summary>
    /// Gets the VGA attribute controller.
    /// </summary>
    public AttributeController AttributeController { get; } = new();

    /// <summary>
    /// Gets a pointer to the emulated video RAM.
    /// </summary>
    public nint VideoRam { get; }

    /// <summary>
    /// Gets the text-mode display instance.
    /// </summary>
    public TextConsole TextConsole { get; }

    /// <summary>
    /// Occurs when the emulated display mode has changed. 
    /// </summary>
    public event EventHandler<VideoModeChangedEventArgs>? VideoModeChanged;

    public void SetVramByte(uint address, byte value) {
        CurrentMode.SetVramByte(address, value);
    }

    public void Render(uint address, object width, object height, nint pixelsAddress) {
        _presenter ??= GetPresenter();
        _presenter.Update(pixelsAddress);
    }

    private void OnVideoModeChanged(object? sender, VideoModeChangedEventArgs e) {
        _presenter = GetPresenter();
    }

    public AeonCard(Machine machine, ILoggerService loggerService, IGui? gui, Configuration configuration) : base(machine) {
        _bios = machine.Bios;
        _logger = loggerService;
        _gui = gui;
        _configuration = configuration;
        
        TextConsole =  new TextConsole(this, _bios.ScreenColumns, _bios.ScreenRows);
        FillDispatchTable();

        unsafe
        {
            VideoRam = new nint(NativeMemory.AllocZeroed(TotalVramBytes));
        }
        VideoModeChanged += OnVideoModeChanged;
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback.Callback(0x00, SetVideoMode));
        _dispatchTable.Add(0x01, new Callback.Callback(0x01, SetCursorType));
        _dispatchTable.Add(0x02, new Callback.Callback(0x02, SetCursorPosition));
        _dispatchTable.Add(0x03, new Callback.Callback(0x03, GetCursorPosition));
        _dispatchTable.Add(0x05, new Callback.Callback(0x05, SelectActiveDisplayPage));
        _dispatchTable.Add(0x06, new Callback.Callback(0x06, ScrollPageUp));
        _dispatchTable.Add(0x07, new Callback.Callback(0x07, ScrollPageDown));
        _dispatchTable.Add(0x08, new Callback.Callback(0x08, ReadCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x09, new Callback.Callback(0x09, WriteCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x0A, new Callback.Callback(0x0A, WriteCharacterAtCursor));
        _dispatchTable.Add(0x0B, new Callback.Callback(0x0B, SetColorPaletteOrBackGroundColor));
        _dispatchTable.Add(0x0E, new Callback.Callback(0x0E, WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback.Callback(0x0F, GetVideoMode));
        _dispatchTable.Add(0x10, new Callback.Callback(0x10, GetSetPaletteRegisters));
        _dispatchTable.Add(0x11, new Callback.Callback(0x11, CharacterGeneratorRoutine));
        _dispatchTable.Add(0x12, new Callback.Callback(0x12, VideoSubsystemConfiguration));
        _dispatchTable.Add(0x1A, new Callback.Callback(0x1A, VideoDisplayCombination));
        _dispatchTable.Add(0x1B, new Callback.Callback(0x1B, GetFunctionalityInfo));
    }

    private void GetFunctionalityInfo() {
        throw new NotImplementedException();
    }

    private void VideoDisplayCombination() {
        if (_state.AL == 0x00) {
            _state.AL = 0x1A; // Function supported
            _state.BL = _bios.DisplayCombinationCode; // Primary display
            _state.BH = 0x00; // No secondary display
        } else if (_state.AL == 0x01) {
            _state.AL = 0x1A; // Function supported
            _bios.DisplayCombinationCode = _state.BL;
        }
    }

    private void VideoSubsystemConfiguration() {
        throw new NotImplementedException();
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
    
    /// <summary>
    /// Returns the address in memory where the specified font is stored.
    /// </summary>
    /// <param name="fontType">One of the <see cref="FontType"/>s</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public SegmentedAddress GetFontAddress(FontType fontType) {
        return _fonts.GetOrAdd(fontType, LoadFont);
    }
    
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

    private void GetFontInformation() {
        SegmentedAddress address = _state.BH switch {
            0x00 => new SegmentedAddress(_memory.GetUint16(0x1F * 4 + 2), _memory.GetUint16(0x1F * 4)),
            0x01 => new SegmentedAddress(_memory.GetUint16(0x43 * 4 + 2), _memory.GetUint16(0x43 * 4)),
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
    }

    private void GetSetPaletteRegisters() {
        throw new NotImplementedException();
    }

    private void GetVideoMode() {
        _state.AH = _bios.ScreenColumns;
        _state.AL = _bios.VideoMode;
        _state.BH = _bios.CurrentVideoPage;
    }

    private void WriteTextInTeletypeMode() {
        throw new NotImplementedException();
    }

    private void SetColorPaletteOrBackGroundColor() {
        throw new NotImplementedException();
    }

    private void WriteCharacterAtCursor() {
        throw new NotImplementedException();
    }

    private void WriteCharacterAndAttributeAtCursor() {
        throw new NotImplementedException();
    }

    private void ReadCharacterAndAttributeAtCursor() {
        throw new NotImplementedException();
    }

    private void ScrollPageDown() {
        throw new NotImplementedException();
    }

    private void ScrollPageUp() {
        throw new NotImplementedException();
    }

    private void SelectActiveDisplayPage() {
        throw new NotImplementedException();
    }

    private void GetCursorPosition() {
        throw new NotImplementedException();
    }

    private void SetCursorPosition() {
        throw new NotImplementedException();
    }

    private void SetCursorType() {
        throw new NotImplementedException();
    }

    private void SetVideoMode() {
        
        VideoModeId id = (VideoModeId)_state.AL;
        VideoMode mode;

        switch (id)
        {
            case VideoModeId.ColorText40x25x4:
                mode = new TextMode(40, 25, 8, this);
                break;

            case VideoModeId.ColorText80x25x4:
            case VideoModeId.MonochromeText80x25x4:
                mode = new TextMode(80, 25, VerticalTextResolution, this);
                break;

            case VideoModeId.ColorGraphics320x200x2A:
            case VideoModeId.ColorGraphics320x200x2B:
                mode = new CgaMode4(this);
                break;

            case VideoModeId.ColorGraphics320x200x4:
                mode = new EgaVga16(320, 200, 8, this);
                break;

            case VideoModeId.ColorGraphics640x200x4:
                mode = new EgaVga16(640, 400, 8, this);
                break;

            case VideoModeId.ColorGraphics640x350x4:
                mode = new EgaVga16(640, 350, 8, this);
                break;

            case VideoModeId.Graphics640x480x4:
                mode = new EgaVga16(640, 480, 16, this);
                break;

            case VideoModeId.Graphics320x200x8:
                Sequencer.SequencerMemoryMode = SequencerMemoryMode.Chain4;
                mode = new Vga256(320, 200, this);
                break;

            default:
                throw new NotSupportedException();
        }
        
        _gui?.SetResolution(mode.Width, mode.Height, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));

        SetDisplayMode(mode);
        _bios.VideoMode = _state.AL;
    }
    
    /// <summary>
    /// Initializes a new display mode.
    /// </summary>
    /// <param name="mode">New display mode.</param>
    public void SetDisplayMode(VideoMode mode)
    {
        CurrentMode = mode;
        mode.InitializeMode(this);
        Graphics.WriteRegister(GraphicsRegister.ColorDontCare, 0x0F);

        if (defaultPaletteLoading)
            Dac.Reset();

        VideoModeChanged?.Invoke(this,new VideoModeChangedEventArgs(true));
    }

    public bool defaultPaletteLoading { get; set; } = true;

    public override byte Index => 0x10;

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    /// <summary>
    /// Specifies one of the int 10h video modes.
    /// </summary>
    internal enum VideoModeId
    {
        /// <summary>
        /// Monochrome 40x25 text mode.
        /// </summary>
        Text40x25x1 = 0x00,
        /// <summary>
        /// Color 40x25 text mode (4-bit).
        /// </summary>
        ColorText40x25x4 = 0x01,
        /// <summary>
        /// Monochrome 80x25 text mode (4-bit).
        /// </summary>
        MonochromeText80x25x4 = 0x02,
        /// <summary>
        /// Color 80x25 text mode (4-bit).
        /// </summary>
        ColorText80x25x4 = 0x03,
        /// <summary>
        /// Color 320x200 graphics mode (2-bit).
        /// </summary>
        ColorGraphics320x200x2A = 0x04,
        /// <summary>
        /// Color 320x200 graphics mode (2-bit).
        /// </summary>
        ColorGraphics320x200x2B = 0x05,
        /// <summary>
        /// Monochrome 640x200 graphics mode (1-bit).
        /// </summary>
        Graphics640x200x1 = 0x06,
        /// <summary>
        /// Monochrome 80x25 text mode (1-bit).
        /// </summary>
        Text80x25x1 = 0x07,
        /// <summary>
        /// Color 320x200 graphics mode (4-bit).
        /// </summary>
        ColorGraphics320x200x4 = 0x0D,
        /// <summary>
        /// Color 640x200 graphics mode (4-bit).
        /// </summary>
        ColorGraphics640x200x4 = 0x0E,
        /// <summary>
        /// Monochrome 640x350 graphics mode (1-bit).
        /// </summary>
        Graphics640x350x1 = 0x0F,
        /// <summary>
        /// Color 640x350 graphics mode (4-bit).
        /// </summary>
        ColorGraphics640x350x4 = 0x10,
        /// <summary>
        /// Monochrome 640x480 graphics mode (1-bit).
        /// </summary>
        Graphics640x480x1 = 0x11,
        /// <summary>
        /// Color 640x480 graphics mode (4-bit).
        /// </summary>
        Graphics640x480x4 = 0x12,
        /// <summary>
        /// Color 320x200 graphics mode (8-bit).
        /// </summary>
        Graphics320x200x8 = 0x13
    }
    
    public Presenter GetPresenter() {
        if (CurrentMode.VideoModeType == VideoModeType.Text) {
            return new TextPresenter(CurrentMode);
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

    public void TickRetrace() {
        // Inactive at tick time, but will become active once the code checks for it.
        _crtStatusRegister = StatusRegisterRetraceInactive;
    }

    public void UpdateScreen() {
        _gui?.UpdateScreen();
    }

    public void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        foreach (int port in InputPorts.Union(OutputPorts)) {
            ioPortDispatcher.AddIOPortHandler(port, this);
        }
    }

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

    public byte ReadByte(int port) {
        switch (port) {
            case Ports.DacAddressReadMode:
                return Dac.ReadIndex;

            case Ports.DacAddressWriteMode:
                return Dac.WriteIndex;

            case Ports.DacData:
                return Dac.Read();

            case Ports.GraphicsControllerAddress:
                return (byte)_graphicsRegister;

            case Ports.GraphicsControllerData:
                return Graphics.ReadRegister(_graphicsRegister);

            case Ports.SequencerAddress:
                return (byte)_sequencerRegister;

            case Ports.SequencerData:
                return Sequencer.ReadRegister(_sequencerRegister);

            case Ports.AttributeAddress:
                return (byte)_attributeRegister;

            case Ports.AttributeData:
                return AttributeController.ReadRegister(_attributeRegister);

            case Ports.CrtControllerAddress:
            case Ports.CrtControllerAddressAlt:
                return (byte)_crtRegister;

            case Ports.CrtControllerData:
            case Ports.CrtControllerDataAlt:
                return CrtController.ReadRegister(_crtRegister);

            case Ports.InputStatus1Read:
            case Ports.InputStatus1ReadAlt:
                _attributeDataMode = false;
                return GetInputStatus1Value();

            default:
                return 0;
        }
    }

    /// <summary>
    /// Returns the current value of the input status 1 register.
    /// </summary>
    /// <returns>Current value of the input status 1 register.</returns>
    private byte GetInputStatus1Value()
    {
        /*
         * bit 7,6: reserved
         * bit 5,4: "video feedback" color debug bits
         * bit 3: Vertical Retrace/Video (VSYNC)
         * bit 2,1: reserved
         * bit 0: Display Enable
         */
        byte res = _crtStatusRegister;
        // Next time we will be called retrace will be active, and this until the retrace tick
        _crtStatusRegister = StatusRegisterRetraceActive;
        return res;
    }

    public ushort ReadWord(int port) {
        throw new NotImplementedException();
    }

    public uint ReadDWord(int port) {
        throw new NotImplementedException();
    }

    public void WriteByte(int port, byte value) {
        switch (port) {
            case Ports.DacAddressReadMode:
                Dac.ReadIndex = value;
                break;

            case Ports.DacAddressWriteMode:
                Dac.WriteIndex = value;
                break;

            case Ports.DacData:
                Dac.Write(value);
                break;

            case Ports.GraphicsControllerAddress:
                _graphicsRegister = (GraphicsRegister)value;
                break;

            case Ports.GraphicsControllerData:
                Graphics.WriteRegister(_graphicsRegister, value);
                break;

            case Ports.SequencerAddress:
                _sequencerRegister = (SequencerRegister)value;
                break;

            case Ports.SequencerData:
                SequencerMemoryMode previousMode = Sequencer.SequencerMemoryMode;
                Sequencer.WriteRegister(_sequencerRegister, value);
                if ((previousMode & SequencerMemoryMode.Chain4) == SequencerMemoryMode.Chain4 &&
                    (Sequencer.SequencerMemoryMode & SequencerMemoryMode.Chain4) == 0)
                    EnterModeX();
                break;

            case Ports.AttributeAddress:
                if (!_attributeDataMode)
                    _attributeRegister = (AttributeControllerRegister)(value & 0x1F);
                else
                    AttributeController.WriteRegister(_attributeRegister, value);
                _attributeDataMode = !_attributeDataMode;
                break;

            case Ports.AttributeData:
                AttributeController.WriteRegister(_attributeRegister, value);
                break;

            case Ports.CrtControllerAddress:
            case Ports.CrtControllerAddressAlt:
                _crtRegister = (CrtControllerRegister)value;
                break;

            case Ports.CrtControllerData:
            case Ports.CrtControllerDataAlt:
                int previousVerticalEnd = CrtController.VerticalDisplayEnd;
                CrtController.WriteRegister(_crtRegister, value);
                if (previousVerticalEnd != CrtController.VerticalDisplayEnd)
                    ChangeVerticalEnd();
                break;
        }
    }

    private void ChangeVerticalEnd() {
        throw new NotImplementedException();
    }

    private void EnterModeX() {
        var mode = new Unchained256(320, 200, this);
        CrtController.Offset = 320 / 8;
        CurrentMode = mode;
        VideoModeChanged?.Invoke(this,new VideoModeChangedEventArgs(false));
    }

    public void WriteWord(int port, ushort value) {
        WriteByte(port, (byte)(value & 0xFF));
        WriteByte(port + 1, (byte)(value >> 8));
    }

    public void WriteDWord(int port, uint value) {
        throw new NotImplementedException();
    }
}