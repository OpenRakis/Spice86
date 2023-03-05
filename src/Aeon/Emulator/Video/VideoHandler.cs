#nullable disable

using Aeon.Emulator.Video.Modes;

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aeon.Emulator.Video
{
    /// <summary>
    /// Provides emulated video and int 10h functions.
    /// </summary>
    internal sealed class VideoHandler : IInterruptHandler, IInputPort, IOutputPort, IDisposable
    {
        /// <summary>
        /// Total number of bytes allocated for video RAM.
        /// </summary>
        public const int TotalVramBytes = 1024 * 1024;
        /// <summary>
        /// Segment of the VGA static functionality table.
        /// </summary>
        public const ushort StaticFunctionalityTableSegment = 0x0100;

        private static readonly long RefreshRate = (long)((1000.0 / 60.0) * InterruptTimer.StopwatchTicksPerMillisecond);
        private static readonly long VerticalBlankingTime = RefreshRate / 40;
        private static readonly long HorizontalPeriod = (long)((1000.0 / 60.0) / 480.0 * InterruptTimer.StopwatchTicksPerMillisecond);
        private static readonly long HorizontalBlankingTime = HorizontalPeriod / 2;

        private bool disposed;
        private GraphicsRegister graphicsRegister;
        private SequencerRegister sequencerRegister;
        private AttributeControllerRegister attributeRegister;
        private CrtControllerRegister crtRegister;
        private bool attributeDataMode;
        private bool defaultPaletteLoading = true;
        private int verticalTextResolution = 16;

        public VideoHandler(VirtualMachine vm)
        {
            VirtualMachine = vm;
            unsafe
            {
                VideoRam = new IntPtr(NativeMemory.AllocZeroed(TotalVramBytes));
            }

            InitializeStaticFunctionalityTable();

            TextConsole = new TextConsole(this, vm.PhysicalMemory.Bios);
            this.SetDisplayMode(VideoMode10.ColorText80x25x4);
        }

        ~VideoHandler() => InternalDispose();

        /// <summary>
        /// Gets the current display mode.
        /// </summary>
        public VideoMode CurrentMode { get; private set; }
        /// <summary>
        /// Gets the text-mode display instance.
        /// </summary>
        public TextConsole TextConsole { get; }
        /// <summary>
        /// Gets the VGA DAC.
        /// </summary>
        public Dac Dac { get; } = new Dac();
        /// <summary>
        /// Gets the VGA attribute controller.
        /// </summary>
        public AttributeController AttributeController { get; } = new AttributeController();
        /// <summary>
        /// Gets the VGA graphics controller.
        /// </summary>
        public Graphics Graphics { get; } = new Graphics();
        /// <summary>
        /// Gets the VGA sequencer.
        /// </summary>
        public Sequencer Sequencer { get; } = new Sequencer();
        /// <summary>
        /// Gets the VGA CRT controller.
        /// </summary>
        public CrtController CrtController { get; } = new CrtController();
        /// <summary>
        /// Gets a pointer to the emulated video RAM.
        /// </summary>
        public IntPtr VideoRam { get; }
        /// <summary>
        /// Gets the virtual machine instance which owns the VideoHandler.
        /// </summary>
        // public VirtualMachine VirtualMachine { get; }

        IEnumerable<InterruptHandlerInfo> IInterruptHandler.HandledInterrupts => new InterruptHandlerInfo[] { 0x10 };
        void IInterruptHandler.HandleInterrupt(int interrupt)
        {
            switch (VirtualMachine.Processor.AH)
            {
                case Functions.GetVideoMode:
                    VirtualMachine.Processor.AH = VirtualMachine.PhysicalMemory.Bios.ScreenColumns;
                    VirtualMachine.Processor.AL = (byte)VirtualMachine.PhysicalMemory.Bios.VideoMode;
                    break;

                case Functions.ScrollActivePageUp:
                    //if(vm.Processor.AL == 0)
                    //    textDisplay.Clear();
                    //else
                    {
                        byte foreground = (byte)((VirtualMachine.Processor.BX >> 8) & 0x0F);
                        byte background = (byte)((VirtualMachine.Processor.BX >> 12) & 0x0F);
                        TextConsole.ScrollTextUp(VirtualMachine.Processor.CL, VirtualMachine.Processor.CH, VirtualMachine.Processor.DL, VirtualMachine.Processor.DH, VirtualMachine.Processor.AL, foreground, background);
                    }
                    break;

                case Functions.EGA:
                    switch (VirtualMachine.Processor.BL)
                    {
                        case Functions.EGA_GetInfo:
                            VirtualMachine.Processor.BX = 0x03; // 256k installed
                            VirtualMachine.Processor.CX = 0x09; // EGA switches set
                            break;

                        case Functions.EGA_SelectVerticalResolution:
                            if (VirtualMachine.Processor.AL == 0)
                                verticalTextResolution = 8;
                            else if (VirtualMachine.Processor.AL == 1)
                                verticalTextResolution = 14;
                            else
                                verticalTextResolution = 16;
                            VirtualMachine.Processor.AL = 0x12; // Success
                            break;

                        case Functions.EGA_PaletteLoading:
                            defaultPaletteLoading = VirtualMachine.Processor.AL == 0;
                            break;

                        default:
                            Debug.WriteLine("Video command {0:X2}, BL={1:X2}h not implemented.", Functions.EGA, VirtualMachine.Processor.BL);
                            break;
                    }
                    break;

                case Functions.ReadCharacterAndAttributeAtCursor:
                    VirtualMachine.Processor.AX = (short)TextConsole.GetCharacter(TextConsole.CursorPosition.X, TextConsole.CursorPosition.Y);
                    break;

                case Functions.WriteCharacterAndAttributeAtCursor:
                    TextConsole.Write(VirtualMachine.Processor.AL, (byte)(VirtualMachine.Processor.BL & 0x0F), (byte)(VirtualMachine.Processor.BL >> 4), false);
                    break;

                case Functions.WriteCharacterAtCursor:
                    TextConsole.Write(VirtualMachine.Processor.AL);
                    break;

                case Functions.GetDisplayCombinationCode:
                    VirtualMachine.Processor.AL = 0x1A;
                    VirtualMachine.Processor.BX = 0x0008;
                    break;

                case Functions.SetVideoMode:
                    SetDisplayMode((VideoMode10)VirtualMachine.Processor.AL);
                    break;

                case Functions.SetCursorPosition:
                    TextConsole.CursorPosition = new Point(VirtualMachine.Processor.DL, VirtualMachine.Processor.DH);
                    break;

                case Functions.SetCursorType:
                    SetCursorShape(VirtualMachine.Processor.CH, VirtualMachine.Processor.CL);
                    break;

                case Functions.SelectActiveDisplayPage:
                    CurrentMode.ActiveDisplayPage = VirtualMachine.Processor.AL;
                    break;

                case Functions.Palette:
                    switch (VirtualMachine.Processor.AL)
                    {
                        case Functions.Palette_SetSingleRegister:
                            SetEgaPaletteRegister(VirtualMachine.Processor.BL, VirtualMachine.Processor.BH);
                            break;

                        case Functions.Palette_SetBorderColor:
                            // Ignore for now.
                            break;

                        case Functions.Palette_SetAllRegisters:
                            SetAllEgaPaletteRegisters();
                            break;

                        case Functions.Palette_ReadSingleDacRegister:
                            // These are commented out because they cause weird issues sometimes.
                            //vm.Processor.DH = (byte)((dac.Palette[vm.Processor.BL] >> 18) & 0xCF);
                            //vm.Processor.CH = (byte)((dac.Palette[vm.Processor.BL] >> 10) & 0xCF);
                            //vm.Processor.CL = (byte)((dac.Palette[vm.Processor.BL] >> 2) & 0xCF);
                            break;

                        case Functions.Palette_SetSingleDacRegister:
                            Dac.SetColor(VirtualMachine.Processor.BL, VirtualMachine.Processor.DH, VirtualMachine.Processor.CH, VirtualMachine.Processor.CL);
                            break;

                        case Functions.Palette_SetDacRegisters:
                            SetDacRegisters();
                            break;

                        case Functions.Palette_ReadDacRegisters:
                            ReadDacRegisters();
                            break;

                        case Functions.Palette_ToggleBlink:
                            // Blinking is not emulated.
                            break;

                        case Functions.Palette_SelectDacColorPage:
                            Debug.WriteLine("Select DAC color page");
                            break;

                        default:
                            throw new NotImplementedException(string.Format("Video command 10{0:X2}h not implemented.", VirtualMachine.Processor.AL));
                    }
                    break;

                case Functions.GetCursorPosition:
                    VirtualMachine.Processor.CH = 14;
                    VirtualMachine.Processor.CL = 15;
                    VirtualMachine.Processor.DH = (byte)TextConsole.CursorPosition.Y;
                    VirtualMachine.Processor.DL = (byte)TextConsole.CursorPosition.X;
                    break;

                case Functions.WriteTextInTeletypeMode:
                    TextConsole.Write(VirtualMachine.Processor.AL);
                    break;

                case Functions.GetFunctionalityInfo:
                    GetFunctionalityInfo();
                    break;

                case 0xEF:
                    VirtualMachine.Processor.DX = -1;
                    break;

                case 0xFE:
                    break;

                case Functions.Font:
                    switch (VirtualMachine.Processor.AL)
                    {
                        case Functions.Font_GetFontInfo:
                            GetFontInfo();
                            break;

                        case Functions.Font_Load8x8:
                            SwitchTo80x50TextMode();
                            break;

                        case Functions.Font_Load8x16:
                            break;

                        default:
                            throw new NotImplementedException($"Video command 11{VirtualMachine.Processor.AL:X2}h not implemented.");
                    }
                    break;

                case Functions.Video:
                    switch (VirtualMachine.Processor.BL)
                    {
                        case Functions.Video_SetBackgroundColor:
                            break;

                        case Functions.Video_SetPalette:
                            Debug.WriteLine("CGA set palette not implemented.");
                            break;
                    }
                    break;

                default:
                    Debug.WriteLine($"Video command {VirtualMachine.Processor.AH:X2}h not implemented.");
                    break;
            }
        }

        IEnumerable<int> IInputPort.InputPorts
        {
            get
            {
                return new SortedSet<int>
                {
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
            }
        }
        public byte ReadByte(int port)
        {
            switch (port)
            {
                case Ports.DacAddressReadMode:
                    return Dac.ReadIndex;

                case Ports.DacAddressWriteMode:
                    return Dac.WriteIndex;

                case Ports.DacData:
                    return Dac.Read();

                case Ports.GraphicsControllerAddress:
                    return (byte)graphicsRegister;

                case Ports.GraphicsControllerData:
                    return Graphics.ReadRegister(graphicsRegister);

                case Ports.SequencerAddress:
                    return (byte)sequencerRegister;

                case Ports.SequencerData:
                    return Sequencer.ReadRegister(sequencerRegister);

                case Ports.AttributeAddress:
                    return (byte)attributeRegister;

                case Ports.AttributeData:
                    return AttributeController.ReadRegister(attributeRegister);

                case Ports.CrtControllerAddress:
                case Ports.CrtControllerAddressAlt:
                    return (byte)crtRegister;

                case Ports.CrtControllerData:
                case Ports.CrtControllerDataAlt:
                    return CrtController.ReadRegister(crtRegister);

                case Ports.InputStatus1Read:
                case Ports.InputStatus1ReadAlt:
                    attributeDataMode = false;
                    return GetInputStatus1Value();

                default:
                    return 0;
            }
        }
        public ushort ReadWord(int port) => ReadByte(port);

        IEnumerable<int> IOutputPort.OutputPorts
        {
            get
            {
                return new SortedSet<int>
                {
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
            }
        }
        public void WriteByte(int port, byte value)
        {
            switch (port)
            {
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
                    graphicsRegister = (GraphicsRegister)value;
                    break;

                case Ports.GraphicsControllerData:
                    Graphics.WriteRegister(graphicsRegister, value);
                    break;

                case Ports.SequencerAddress:
                    sequencerRegister = (SequencerRegister)value;
                    break;

                case Ports.SequencerData:
                    var previousMode = Sequencer.SequencerMemoryMode;
                    Sequencer.WriteRegister(sequencerRegister, value);
                    if ((previousMode & SequencerMemoryMode.Chain4) == SequencerMemoryMode.Chain4 && (Sequencer.SequencerMemoryMode & SequencerMemoryMode.Chain4) == 0)
                        EnterModeX();
                    break;

                case Ports.AttributeAddress:
                    if (!attributeDataMode)
                        attributeRegister = (AttributeControllerRegister)(value & 0x1F);
                    else
                        AttributeController.WriteRegister(attributeRegister, value);
                    attributeDataMode = !attributeDataMode;
                    break;

                case Ports.AttributeData:
                    AttributeController.WriteRegister(attributeRegister, value);
                    break;

                case Ports.CrtControllerAddress:
                case Ports.CrtControllerAddressAlt:
                    crtRegister = (CrtControllerRegister)value;
                    break;

                case Ports.CrtControllerData:
                case Ports.CrtControllerDataAlt:
                    int previousVerticalEnd = CrtController.VerticalDisplayEnd;
                    CrtController.WriteRegister(crtRegister, value);
                    if (previousVerticalEnd != CrtController.VerticalDisplayEnd)
                        ChangeVerticalEnd();
                    break;
            }
        }

        // void IVirtualDevice.DeviceRegistered(VirtualMachine vm) => vm.RegisterVirtualDevice(vbe);

        /// <summary>
        /// Reads a byte from video RAM.
        /// </summary>
        /// <param name="offset">Offset of byte to read.</param>
        /// <returns>Byte read from video RAM.</returns>
        public byte GetVramByte(uint offset) => CurrentMode.GetVramByte(offset);
        /// <summary>
        /// Sets a byte in video RAM to a specified value.
        /// </summary>
        /// <param name="offset">Offset of byte to set.</param>
        /// <param name="value">Value to write.</param>
        public void SetVramByte(uint offset, byte value) => CurrentMode.SetVramByte(offset, value);
        /// <summary>
        /// Reads a word from video RAM.
        /// </summary>
        /// <param name="offset">Offset of word to read.</param>
        /// <returns>Word read from video RAM.</returns>
        public ushort GetVramWord(uint offset) => CurrentMode.GetVramWord(offset);
        /// <summary>
        /// Sets a word in video RAM to a specified value.
        /// </summary>
        /// <param name="offset">Offset of word to set.</param>
        /// <param name="value">Value to write.</param>
        public void SetVramWord(uint offset, ushort value) => CurrentMode.SetVramWord(offset, value);
        /// <summary>
        /// Reads a doubleword from video RAM.
        /// </summary>
        /// <param name="offset">Offset of doubleword to read.</param>
        /// <returns>Doubleword read from video RAM.</returns>
        public uint GetVramDWord(uint offset) => CurrentMode.GetVramDWord(offset);
        /// <summary>
        /// Sets a doubleword in video RAM to a specified value.
        /// </summary>
        /// <param name="offset">Offset of doubleword to set.</param>
        /// <param name="value">Value to write.</param>
        public void SetVramDWord(uint offset, uint value) => CurrentMode.SetVramDWord(offset, value);
        /// <summary>
        /// Initializes a new display mode.
        /// </summary>
        /// <param name="videoMode">New display mode.</param>
        public void SetDisplayMode(VideoMode10 videoMode)
        {
            VirtualMachine.PhysicalMemory.Bios.VideoMode = videoMode;
            VideoMode mode;

            switch (videoMode)
            {
                case VideoMode10.ColorText40x25x4:
                    mode = new TextMode(40, 25, 8, this);
                    break;

                case VideoMode10.ColorText80x25x4:
                case VideoMode10.MonochromeText80x25x4:
                    mode = new TextMode(80, 25, verticalTextResolution, this);
                    break;

                case VideoMode10.ColorGraphics320x200x2A:
                case VideoMode10.ColorGraphics320x200x2B:
                    mode = new CgaMode4(this);
                    break;

                case VideoMode10.ColorGraphics320x200x4:
                    mode = new EgaVga16(320, 200, 8, this);
                    break;

                case VideoMode10.ColorGraphics640x200x4:
                    mode = new EgaVga16(640, 400, 8, this);
                    break;

                case VideoMode10.ColorGraphics640x350x4:
                    mode = new EgaVga16(640, 350, 8, this);
                    break;

                case VideoMode10.Graphics640x480x4:
                    mode = new EgaVga16(640, 480, 16, this);
                    break;

                case VideoMode10.Graphics320x200x8:
                    Sequencer.SequencerMemoryMode = SequencerMemoryMode.Chain4;
                    mode = new Vga256(320, 200, this);
                    break;

                default:
                    throw new NotSupportedException();
            }

            SetDisplayMode(mode);
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

            VirtualMachine.OnVideoModeChanged(new VideoModeChangedEventArgs(true));
        }

        void IDisposable.Dispose()
        {
            InternalDispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Sets the current mode to unchained mode 13h.
        /// </summary>
        private void EnterModeX()
        {
            var mode = new Unchained256(320, 200, this);
            CrtController.Offset = 320 / 8;
            CurrentMode = mode;
            VirtualMachine.OnVideoModeChanged(new VideoModeChangedEventArgs(false));
        }
        /// <summary>
        /// Changes the current video mode to match the new value of the vertical end register.
        /// </summary>
        private void ChangeVerticalEnd()
        {
            // this is a hack
            int newEnd = CrtController.VerticalDisplayEnd | ((CrtController.Overflow & (1 << 1)) << 7) | ((CrtController.Overflow & (1 << 6)) << 3);
            if (CurrentMode is Unchained256)
            {
                newEnd /= 2;
            }
            else
            {
                newEnd = newEnd switch
                {
                    223 => 480,
                    184 => 440,
                    _ => newEnd * 2
                };
            }

            CurrentMode.Height = newEnd;
            VirtualMachine.OnVideoModeChanged(new VideoModeChangedEventArgs(false));
        }
        /// <summary>
        /// Sets the current mode to text mode 80x50.
        /// </summary>
        private void SwitchTo80x50TextMode()
        {
            var mode = new TextMode(80, 50, 8, this);
            CurrentMode = mode;
            VirtualMachine.OnVideoModeChanged(new VideoModeChangedEventArgs(false));
        }
        /// <summary>
        /// Sets DAC color registers to values in emulated RAM.
        /// </summary>
        private void SetDacRegisters()
        {
            ushort segment = VirtualMachine.Processor.ES;
            uint offset = (ushort)VirtualMachine.Processor.DX;
            int start = VirtualMachine.Processor.BX;
            int count = VirtualMachine.Processor.CX;

            for (int i = start; i < count; i++)
            {
                byte r = VirtualMachine.PhysicalMemory.GetByte(segment, offset);
                byte g = VirtualMachine.PhysicalMemory.GetByte(segment, offset + 1u);
                byte b = VirtualMachine.PhysicalMemory.GetByte(segment, offset + 2u);

                Dac.SetColor((byte)(start + i), r, g, b);

                offset += 3u;
            }
        }
        /// <summary>
        /// Reads DAC color registers to emulated RAM.
        /// </summary>
        private void ReadDacRegisters()
        {
            ushort segment = VirtualMachine.Processor.ES;
            uint offset = (ushort)VirtualMachine.Processor.DX;
            int start = VirtualMachine.Processor.BX;
            int count = VirtualMachine.Processor.CX;

            for (int i = start; i < count; i++)
            {
                uint r = (Dac.Palette[start + i] >> 18) & 0xCFu;
                uint g = (Dac.Palette[start + i] >> 10) & 0xCFu;
                uint b = (Dac.Palette[start + i] >> 2) & 0xCFu;

                VirtualMachine.PhysicalMemory.SetByte(segment, offset, (byte)r);
                VirtualMachine.PhysicalMemory.SetByte(segment, offset + 1u, (byte)g);
                VirtualMachine.PhysicalMemory.SetByte(segment, offset + 2u, (byte)b);

                offset += 3u;
            }
        }
        /// <summary>
        /// Sets all of the EGA color palette registers to values in emulated RAM.
        /// </summary>
        private void SetAllEgaPaletteRegisters()
        {
            ushort segment = VirtualMachine.Processor.ES;
            uint offset = (ushort)VirtualMachine.Processor.DX;

            for (uint i = 0; i < 16u; i++)
                SetEgaPaletteRegister((int)i, VirtualMachine.PhysicalMemory.GetByte(segment, offset + i));
        }
        /// <summary>
        /// Gets a specific EGA color palette register.
        /// </summary>
        /// <param name="index">Index of color to set.</param>
        /// <param name="color">New value of the color.</param>
        private void SetEgaPaletteRegister(int index, byte color)
        {
            if (VirtualMachine.PhysicalMemory.Bios.VideoMode == VideoMode10.ColorGraphics320x200x4)
                AttributeController.InternalPalette[index & 0x0F] = (byte)(color & 0x0F);
            else
                AttributeController.InternalPalette[index & 0x0F] = color;
        }
        /// <summary>
        /// Gets information about BIOS fonts.
        /// </summary>
        private void GetFontInfo()
        {
            var address = VirtualMachine.Processor.BH switch
            {
                0x00 => VirtualMachine.PhysicalMemory.GetRealModeInterruptAddress(0x1F),
                0x01 => VirtualMachine.PhysicalMemory.GetRealModeInterruptAddress(0x43),
                0x02 or 0x05 => new RealModeAddress(PhysicalMemory.FontSegment, PhysicalMemory.Font8x14Offset),
                0x03 => new RealModeAddress(PhysicalMemory.FontSegment, PhysicalMemory.Font8x8Offset),
                0x04 => new RealModeAddress(PhysicalMemory.FontSegment, PhysicalMemory.Font8x8Offset + 128 * 8),
                _ => new RealModeAddress(PhysicalMemory.FontSegment, PhysicalMemory.Font8x16Offset),
            };

            VirtualMachine.WriteSegmentRegister(SegmentIndex.ES, address.Segment);
            VirtualMachine.Processor.BP = address.Offset;
            VirtualMachine.Processor.CX = (short)CurrentMode.FontHeight;
            VirtualMachine.Processor.DL = VirtualMachine.PhysicalMemory.Bios.ScreenRows;
        }
        /// <summary>
        /// Changes the appearance of the text-mode cursor.
        /// </summary>
        /// <param name="topOptions">Top scan line and options.</param>
        /// <param name="bottom">Bottom scan line.</param>
        private void SetCursorShape(int topOptions, int bottom)
        {
            int mode = (topOptions >> 4) & 3;
            VirtualMachine.IsCursorVisible = mode != 2;
        }
        /// <summary>
        /// Writes values to the static functionality table in emulated memory.
        /// </summary>
        private void InitializeStaticFunctionalityTable()
        {
            var memory = VirtualMachine.PhysicalMemory;
            memory.SetUInt32(StaticFunctionalityTableSegment, 0, 0x000FFFFF); // supports all video modes
            memory.SetByte(StaticFunctionalityTableSegment, 0x07, 0x07); // supports all scanlines
        }
        /// <summary>
        /// Writes a table of information about the current video mode.
        /// </summary>
        private void GetFunctionalityInfo()
        {
            ushort segment = VirtualMachine.Processor.ES;
            ushort offset = VirtualMachine.Processor.DI;

            var memory = VirtualMachine.PhysicalMemory;
            var bios = memory.Bios;

            var cursorPos = TextConsole.CursorPosition;

            memory.SetUInt32(segment, offset, StaticFunctionalityTableSegment << 16); // SFT address
            memory.SetByte(segment, offset + 0x04u, (byte)bios.VideoMode); // video mode
            memory.SetUInt16(segment, offset + 0x05u, bios.ScreenColumns); // columns
            memory.SetUInt32(segment, offset + 0x07u, 0); // regen buffer
            for (uint i = 0; i < 8; i++)
            {
                memory.SetByte(segment, offset + 0x0Bu + i * 2u, (byte)cursorPos.X); // text cursor x
                memory.SetByte(segment, offset + 0x0Cu + i * 2u, (byte)cursorPos.Y); // text cursor y
            }

            memory.SetUInt16(segment, offset + 0x1Bu, 0); // cursor type
            memory.SetByte(segment, offset + 0x1Du, (byte)CurrentMode.ActiveDisplayPage); // active display page
            memory.SetUInt16(segment, offset + 0x1Eu, bios.CrtControllerBaseAddress); // CRTC base address
            memory.SetByte(segment, offset + 0x20u, 0); // current value of port 3x8h
            memory.SetByte(segment, offset + 0x21u, 0); // current value of port 3x9h
            memory.SetByte(segment, offset + 0x22u, bios.ScreenRows); // screen rows
            memory.SetUInt16(segment, offset + 0x23u, (ushort)CurrentMode.FontHeight); // bytes per character
            memory.SetByte(segment, offset + 0x25u, (byte)bios.VideoMode); // active display combination code
            memory.SetByte(segment, offset + 0x26u, (byte)bios.VideoMode); // alternate display combination code
            memory.SetUInt16(segment, offset + 0x27u, (ushort)(CurrentMode.BitsPerPixel * 8)); // number of colors supported in current mode
            memory.SetByte(segment, offset + 0x29u, 4); // number of pages
            memory.SetByte(segment, offset + 0x2Au, 0); // number of active scanlines

            // Indicate success.
            VirtualMachine.Processor.AL = 0x1B;
        }

        private void InternalDispose()
        {
            if (!disposed)
            {
                unsafe
                {
                    if (VideoRam != IntPtr.Zero)
                        NativeMemory.Free(VideoRam.ToPointer());
                }

                disposed = true;
            }
        }

        /// <summary>
        /// Returns the current value of the input status 1 register.
        /// </summary>
        /// <returns>Current value of the input status 1 register.</returns>
        private static byte GetInputStatus1Value()
        {
            uint value = InterruptTimer.IsInRealtimeInterval(VerticalBlankingTime, RefreshRate) ? 0x09u : 0x00u;
            if (InterruptTimer.IsInRealtimeInterval(HorizontalBlankingTime, HorizontalPeriod))
                value |= 0x01u;

            return (byte)value;
        }
    }
}
