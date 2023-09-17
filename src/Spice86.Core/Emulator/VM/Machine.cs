namespace Spice86.Core.Emulator.VM;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.Sound;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Centralizes classes instances that should live while the CPU is running.
/// </summary>
public sealed class Machine : IDisposable, IDebuggableComponent {
    private bool _disposed;
    
    /// <summary>
    /// Memory mapped BIOS values.
    /// </summary>
    public BiosDataArea BiosDataArea { get; }

    /// <summary>
    /// INT11H handler.
    /// </summary>
    public BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; }

    /// <summary>
    /// INT9H handler.
    /// </summary>
    public BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; }

    /// <summary>
    /// Handles all the callbacks, most notably interrupts.
    /// </summary>
    public CallbackHandler CallbackHandler { get; }
    
    private InterruptInstaller InterruptInstaller { get; }

    private AssemblyRoutineInstaller AssemblyRoutineInstaller { get; }

    /// <summary>
    /// The emulated CPU.
    /// </summary>
    public Cpu Cpu { get; }

    /// <summary>
    /// The emulated CPU state.
    /// </summary>
    public State CpuState { get; }

    /// <summary>
    /// DOS Services.
    /// </summary>
    public Dos Dos { get; }

    /// <summary>
    /// The Gravis Ultrasound sound card.
    /// </summary>
    public GravisUltraSound GravisUltraSound { get; }

    /// <summary>
    /// Gives the port read or write to the registered handler.
    /// </summary>
    public IOPortDispatcher IoPortDispatcher { get; }

    /// <summary>
    /// A gameport joystick
    /// </summary>
    public Joystick Joystick { get; }

    /// <summary>
    /// An IBM PC Keyboard
    /// </summary>
    public Keyboard Keyboard { get; }

    /// <summary>
    /// INT16H handler.
    /// </summary>
    public KeyboardInt16Handler KeyboardInt16Handler { get; }

    /// <summary>
    /// Contains all the breakpoints
    /// </summary>
    public MachineBreakpoints MachineBreakpoints { get; }

    /// <summary>
    /// The memory bus.
    /// </summary>
    public IMemory Memory { get; }

    /// <summary>
    /// The General MIDI (MPU-401) or MT-32 device.
    /// </summary>
    public Midi MidiDevice { get; }

    /// <summary>
    /// PC Speaker device.
    /// </summary>
    public PcSpeaker PcSpeaker { get; }

    /// <summary>
    /// The dual programmable interrupt controllers.
    /// </summary>
    public DualPic DualPic { get; }

    /// <summary>
    /// The Sound Blaster card.
    /// </summary>
    public SoundBlaster SoundBlaster { get; }

    /// <summary>
    /// INT15H handler.
    /// </summary>
    public SystemBiosInt15Handler SystemBiosInt15Handler { get; }

    /// <summary>
    /// INT1A handler.
    /// </summary>
    public SystemClockInt1AHandler SystemClockInt1AHandler { get; }

    /// <summary>
    /// The Programmable Interrupt Timer
    /// </summary>
    public Timer Timer { get; }

    /// <summary>
    /// INT8H handler.
    /// </summary>
    public TimerInt8Handler TimerInt8Handler { get; }

    /// <summary>
    /// The VGA Card.
    /// </summary>
    public IVideoCard VgaCard { get; }
    
    /// <summary>
    /// The VGA Registers
    /// </summary>
    public IVideoState VgaRegisters { get; set; }
    
    /// <summary>
    /// The VGA port handler
    /// </summary>
    public IIOPortHandler VgaIoPortHandler { get; }

    /// <summary>
    /// The class that handles converting video memory to a bitmap
    /// </summary>
    public readonly IVgaRenderer VgaRenderer;
    
    /// <summary>
    /// The Video BIOS interrupt handler.
    /// </summary>
    public IVideoInt10Handler VideoInt10Handler { get; }
    
    /// <summary>
    /// The Video Rom containing fonts and other data.
    /// </summary>
    public VgaRom VgaRom { get; }

    /// <summary>
    /// The DMA controller.
    /// </summary>
    public DmaController DmaController { get; }
    
    /// <summary>
    /// The OPL3 FM Synth chip.
    /// </summary>
    public OPL3FM OPL3FM { get; }

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    public Machine(IGui? gui, ILoggerService loggerService, CounterConfigurator counterConfigurator, ExecutionFlowRecorder executionFlowRecorder, Configuration configuration, bool recordData) {
        Memory = new Memory(new Ram(A20Gate.EndOfHighMemoryArea), configuration.A20Gate, configuration.InitializeDOS is true);
        BiosDataArea = new BiosDataArea(Memory);
        CpuState = new State();
        DualPic = DualPic = new(CpuState, configuration.FailOnUnhandledPort, configuration.InitializeDOS is false, loggerService);
        // Breakpoints
        MachineBreakpoints = new(Memory, CpuState, loggerService);
        IoPortDispatcher = new IOPortDispatcher(CpuState, loggerService, configuration.FailOnUnhandledPort);
        CallbackHandler = new(CpuState, loggerService);

        Cpu = new Cpu(Memory, CpuState, DualPic, IoPortDispatcher, CallbackHandler, MachineBreakpoints, loggerService, executionFlowRecorder, recordData);
        
        // IO devices
        DmaController = new DmaController(Memory, CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(DmaController);

        RegisterIoPortHandler(DualPic);
        
        VgaRegisters = new VideoState();
        VgaIoPortHandler = new VgaIoPortHandler(CpuState, loggerService, VgaRegisters, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(VgaIoPortHandler);

        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(VgaRegisters);
        Memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        VgaRenderer = new Renderer(VgaRegisters, vgaMemory);
        VgaCard = new VgaCard(gui, VgaRenderer, loggerService);

        Timer = new Timer(CpuState, loggerService, DualPic, VgaCard, counterConfigurator, configuration.FailOnUnhandledPort);
        if (gui is not null) {
            gui.ProgrammableIntervalTimer = Timer;
        }
        RegisterIoPortHandler(Timer);
        Keyboard = new Keyboard(CpuState, Memory.A20Gate, DualPic, loggerService, gui, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(Keyboard);
        MouseDevice = new Mouse(CpuState, DualPic, gui, configuration.Mouse, loggerService, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(MouseDevice);
        Joystick = new Joystick(CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(Joystick);
        AudioPlayerFactory audioPlayerFactory = new AudioPlayerFactory(loggerService);
        PcSpeaker = new PcSpeaker(audioPlayerFactory, CpuState, loggerService, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(PcSpeaker);
        OPL3FM = new OPL3FM(audioPlayerFactory, CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(OPL3FM);
        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5);
        SoundBlaster = new SoundBlaster(audioPlayerFactory, CpuState, DmaController, DualPic, gui, configuration.FailOnUnhandledPort, loggerService, soundBlasterHardwareConfig);
        RegisterIoPortHandler(SoundBlaster);
        GravisUltraSound = new GravisUltraSound(CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(GravisUltraSound);
        MidiDevice = new Midi(audioPlayerFactory, CpuState, configuration.Mt32RomsPath, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(MidiDevice);

        // Services
        // memoryAsmWriter is common to InterruptInstaller and AssemblyRoutineInstaller so that they both write at the same address (Bios Segment F000)
        MemoryAsmWriter memoryAsmWriter = new(Memory, new SegmentedAddress(configuration.ProvidedAsmHandlersSegment, 0), CallbackHandler);
        InterruptInstaller = new InterruptInstaller(new InterruptVectorTable(Memory), memoryAsmWriter, Cpu.FunctionHandler);
        AssemblyRoutineInstaller = new AssemblyRoutineInstaller(memoryAsmWriter, Cpu.FunctionHandler);
        
        VgaRom = new VgaRom();
        Memory.RegisterMapping(MemoryMap.VideoBiosSegment << 4, VgaRom.Size, VgaRom);
        VgaFunctions = new VgaFunctionality(Memory, IoPortDispatcher, BiosDataArea, VgaRom,  configuration.InitializeDOS is true);
        VideoInt10Handler = new VgaBios(Memory, Cpu, VgaFunctions, BiosDataArea, loggerService);
        
        TimerInt8Handler = new TimerInt8Handler(Memory, Cpu, DualPic, Timer, BiosDataArea, loggerService);
        BiosKeyboardInt9Handler = new BiosKeyboardInt9Handler(Memory, Cpu, DualPic, Keyboard, BiosDataArea, loggerService);
        
        BiosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(Memory, Cpu, loggerService);
        SystemBiosInt15Handler = new SystemBiosInt15Handler(Memory, Cpu, Memory.A20Gate, loggerService);
        KeyboardInt16Handler = new KeyboardInt16Handler(Memory, Cpu, loggerService, BiosKeyboardInt9Handler.BiosKeyboardBuffer);

        SystemClockInt1AHandler = new SystemClockInt1AHandler(Memory, Cpu, loggerService, TimerInt8Handler);

        MouseDriver = new MouseDriver(Cpu, Memory, MouseDevice, gui, VgaFunctions, loggerService);
        Dos = new Dos(Memory, Cpu, KeyboardInt16Handler, VgaFunctions, configuration.CDrive, configuration.Exe, loggerService);

        if (configuration.InitializeDOS is not false) {
            // Register the interrupt handlers
            RegisterInterruptHandler(VideoInt10Handler);
            RegisterInterruptHandler(TimerInt8Handler);
            RegisterInterruptHandler(BiosKeyboardInt9Handler);
            RegisterInterruptHandler(BiosEquipmentDeterminationInt11Handler);
            RegisterInterruptHandler(SystemBiosInt15Handler);
            RegisterInterruptHandler(KeyboardInt16Handler);
            RegisterInterruptHandler(SystemClockInt1AHandler);
            RegisterInterruptHandler(Dos.DosInt20Handler);
            RegisterInterruptHandler(Dos.DosInt21Handler);
            RegisterInterruptHandler(Dos.DosInt2FHandler);

            // Initialize DOS.
            Dos.Initialize(SoundBlaster, CpuState, configuration.Ems);
            if (Dos.Ems is not null) {
                RegisterInterruptHandler(Dos.Ems);
            }

            var mouseInt33Handler = new MouseInt33Handler(Memory, Cpu, loggerService, MouseDriver);
            RegisterInterruptHandler(mouseInt33Handler);

            var mouseIrq12Handler = new BiosMouseInt74Handler(DualPic, Memory);
            RegisterInterruptHandler(mouseIrq12Handler);

            SegmentedAddress mouseDriverAddress = AssemblyRoutineInstaller.InstallAssemblyRoutine(MouseDriver);
            mouseIrq12Handler.SetMouseDriverAddress(mouseDriverAddress);
        }
    }

    /// <summary>
    /// The mouse device hardware abstraction.
    /// </summary>
    public IMouseDevice MouseDevice { get; }

    /// <summary>
    /// The mouse driver.
    /// </summary>
    public IMouseDriver MouseDriver { get; }

    /// <summary>
    /// Defines all VGA high level functions, such as writing text to the screen.
    /// </summary>
    public IVgaFunctionality VgaFunctions { get; set; }

    /// <summary>
    /// Registers an interrupt handler
    /// </summary>
    /// <param name="interruptHandler">The interrupt handler to install.</param>
    public void RegisterInterruptHandler(IInterruptHandler interruptHandler) => InterruptInstaller.InstallInterruptHandler(interruptHandler);

    /// <summary>
    /// Registers a I/O port handler, such as a sound card.
    /// </summary>
    /// <param name="ioPortHandler">The I/O port handler.</param>
    /// <exception cref="ArgumentException"></exception>
    public void RegisterIoPortHandler(IIOPortHandler ioPortHandler) => ioPortHandler.InitPortHandlers(IoPortDispatcher);

    /// <summary>
    /// Releases all resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                MidiDevice.Dispose();
                SoundBlaster.Dispose();
                DmaController.Dispose();
                OPL3FM.Dispose();
                PcSpeaker.Dispose();
                MachineBreakpoints.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void Accept(IEmulatorDebugger emulatorDebugger) {
        Cpu.Accept(emulatorDebugger);
        ((IDebuggableComponent)VgaCard).Accept(emulatorDebugger);
        ((IDebuggableComponent)VgaRenderer).Accept(emulatorDebugger);
        ((IDebuggableComponent)VgaRegisters).Accept(emulatorDebugger);
        ((IDebuggableComponent)Memory).Accept(emulatorDebugger);
    }
}