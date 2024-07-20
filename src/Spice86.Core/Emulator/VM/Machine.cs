namespace Spice86.Core.Emulator.VM;

using MeltySynth;

using Mt32emu;

using Spice86.Core.Backend.Audio.PortAudio;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Sound.Midi.MT32;
using Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Linq;

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
    /// The emulated CPU.
    /// </summary>
    public CfgCpu CfgCpu { get; }

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
    /// The General MIDI or MT-32 device.
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
    /// INT12H handler.
    /// </summary>
    public SystemBiosInt12Handler SystemBiosInt12Handler { get; }

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
    public VgaCard VgaCard { get; }
    
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
    /// The internal software mixer for all sound channels.
    /// </summary>
    public SoftwareMixer SoftwareMixer { get; }
    
    /// <summary>
    /// The size of the conventional memory in kilobytes.
    /// </summary>
    public const uint ConventionalMemorySizeKb = 640;
    
    /// <summary>
    /// Returns the appropriate <see cref="CounterActivator"/> based on the configuration.
    /// </summary>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="loggerService">The service used for logging.</param>
    /// <param name="configuration">The emulator's configuration.</param>
    /// <returns>The appropriate <see cref="CyclesCounterActivator"/> or <see cref="TimeCounterActivator"/></returns>
    private static CounterActivator CreateCounterActivator(State state, ILoggerService loggerService, Configuration configuration) {
        const long DefaultInstructionsPerSecond = 1000000L;
        long? instructionsPerSecond = configuration.InstructionsPerSecond;
        if (instructionsPerSecond == null && configuration.GdbPort != null) {
            // With GDB, force to instructions per seconds as time based timers could perturb steps
            instructionsPerSecond = DefaultInstructionsPerSecond;
            if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                loggerService.Warning("Forcing Counter to use instructions per seconds since we are in GDB mode. If speed is too slow or too fast adjust the --InstructionsPerSecond parameter");
            }
        }
        if (instructionsPerSecond != null) {
            return new CyclesCounterActivator(state, instructionsPerSecond.Value, configuration.TimeMultiplier);
        }
        return new TimeCounterActivator(configuration.TimeMultiplier);
    }

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    public Machine(IGui? gui, IMemory memory,  MachineBreakpoints machineBreakpoints, State cpuState, IOPortDispatcher ioPortDispatcher, ILoggerService loggerService, ExecutionFlowRecorder executionFlowRecorder, Configuration configuration, bool recordData) {
        CpuState = cpuState;
        Memory = memory;
        bool initializeResetVector = configuration.InitializeDOS is true;
        if (initializeResetVector) {
            // Put HLT instruction at the reset address
            Memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
        IoPortDispatcher = ioPortDispatcher;
        BiosDataArea = new BiosDataArea(Memory) {
            ConventionalMemorySizeKb = (ushort)Math.Clamp(Memory.Ram.Size / 1024, 0, ConventionalMemorySizeKb)
        };
        DualPic = new(new Pic(loggerService), new Pic(loggerService), CpuState, configuration.FailOnUnhandledPort, configuration.InitializeDOS is false, loggerService);

        MachineBreakpoints = machineBreakpoints;
        IoPortDispatcher = new IOPortDispatcher(CpuState, loggerService, configuration.FailOnUnhandledPort);
        CallbackHandler = new(CpuState, loggerService);

        InterruptVectorTable interruptVectorTable = new(Memory);
        Stack stack = new(Memory, CpuState);
        Alu8 alu8 = new(cpuState);
        Alu16 alu16 = new Alu16(cpuState);
        Alu32 alu32 = new Alu32(cpuState);
        FunctionHandler functionHandler = new FunctionHandler(Memory, cpuState, executionFlowRecorder, loggerService, recordData);
        FunctionHandler functionHandlerInExternalInterrupt = new FunctionHandler(Memory, cpuState, executionFlowRecorder, loggerService, recordData);
        Cpu = new Cpu(interruptVectorTable, alu8, alu16, alu32, stack,
            functionHandler, functionHandlerInExternalInterrupt, Memory, CpuState,
            DualPic, IoPortDispatcher, CallbackHandler, MachineBreakpoints,
            loggerService, executionFlowRecorder);
        
        InstructionFieldValueRetriever instructionFieldValueRetriever = new(Memory);
        ModRmExecutor modRmExecutor = new(CpuState, Memory, instructionFieldValueRetriever);
        InstructionExecutionHelper instructionExecutionHelper = new(
            cpuState, Memory, ioPortDispatcher,
            CallbackHandler, interruptVectorTable, stack,
            alu8, alu16, alu32,
            instructionFieldValueRetriever, modRmExecutor, loggerService);
        ExecutionContextManager executionContextManager = new(MachineBreakpoints, new ExecutionContext());
        NodeLinker nodeLinker = new();
        InstructionsFeeder instructionsFeeder = new(new CurrentInstructions(Memory, MachineBreakpoints), new InstructionParser(Memory, CpuState), new PreviousInstructions(Memory));
        CfgNodeFeeder cfgNodeFeeder = new(instructionsFeeder, new(nodeLinker, instructionsFeeder), nodeLinker, cpuState);
        CfgCpu = new CfgCpu(instructionExecutionHelper, executionContextManager, cfgNodeFeeder, CpuState, DualPic);

        // IO devices
        DmaController = new DmaController(Memory, CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(DmaController);

        RegisterIoPortHandler(DualPic);

        DacRegisters dacRegisters = new DacRegisters(new ArgbPalette());
        VgaRegisters = new VideoState(dacRegisters, new(
                new(),new(),new()),
                new(new(), new(), new(), new(), new()),
                new(new(), new(), new(), new(), new(), new(), new(), new(), new(), new()),
                new(new(), new(), new(), new(), new(), new()),
                new(new(), new(), new()));
        VgaIoPortHandler = new VgaIoPortHandler(CpuState, loggerService, VgaRegisters, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(VgaIoPortHandler);

        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(VgaRegisters);
        Memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        VgaRenderer = new Renderer(VgaRegisters, vgaMemory);
        VgaCard = new VgaCard(gui, VgaRenderer, loggerService);
        
        Counter firstCounter = new Counter(cpuState, loggerService, CreateCounterActivator(cpuState, loggerService, configuration)) {
            Index = 0
        };
        Counter secondCounter = new Counter(cpuState, loggerService, CreateCounterActivator(cpuState, loggerService, configuration)) {
            Index = 1
        };
        Counter thirdCounter = new Counter(cpuState, loggerService, CreateCounterActivator(cpuState, loggerService, configuration)) {
            Index = 2
        };
        
        Timer = new Timer(CpuState, loggerService, DualPic, firstCounter, secondCounter, thirdCounter, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(Timer);
        Keyboard = new Keyboard(CpuState, Memory.A20Gate, DualPic, loggerService, gui, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(Keyboard);
        MouseDevice = new Mouse(CpuState, DualPic, gui, configuration.Mouse, loggerService, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(MouseDevice);
        Joystick = new Joystick(CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(Joystick);
        
        SoftwareMixer = new(new  AudioPlayerFactory(new PortAudioPlayerFactory(loggerService)));
        
        PcSpeaker = new PcSpeaker(new LatchedUInt16(), new SoundChannel(SoftwareMixer, nameof(PcSpeaker)), CpuState, loggerService, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(PcSpeaker);
        
        SoundChannel fmSynthSoundChannel = new SoundChannel(SoftwareMixer, "SoundBlaster OPL3 FM Synth");
        OPL3FM = new OPL3FM(new FmSynthesizer(48000), fmSynthSoundChannel, CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(OPL3FM);
        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.Sb16);
        SoundChannel pcmSoundChannel = new SoundChannel(SoftwareMixer, "SoundBlaster PCM");
        HardwareMixer hardwareMixer = new HardwareMixer(soundBlasterHardwareConfig, pcmSoundChannel, fmSynthSoundChannel, loggerService);
        DmaChannel eightByteDmaChannel = DmaController.Channels[soundBlasterHardwareConfig.LowDma];
        Dsp dsp = new Dsp(eightByteDmaChannel, DmaController.Channels[soundBlasterHardwareConfig.HighDma], new ADPCM2(),  new ADPCM3(), new ADPCM4());
        SoundBlaster = new SoundBlaster(pcmSoundChannel, hardwareMixer, dsp, eightByteDmaChannel, fmSynthSoundChannel, CpuState, DmaController, DualPic, configuration.FailOnUnhandledPort, loggerService, soundBlasterHardwareConfig);
        RegisterIoPortHandler(SoundBlaster);
        
        GravisUltraSound = new GravisUltraSound(CpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(GravisUltraSound);
        
        // the external MIDI device (external General MIDI or external Roland MT-32).
        MidiDevice midiMapper;
        if (!string.IsNullOrWhiteSpace(configuration.Mt32RomsPath) && File.Exists(configuration.Mt32RomsPath)) {
            midiMapper = new Mt32MidiDevice(new Mt32Context(), new SoundChannel(SoftwareMixer, "MT-32"), configuration.Mt32RomsPath, loggerService);
        } else {
            midiMapper = new GeneralMidiDevice(new Synthesizer(new SoundFont(GeneralMidiDevice.SoundFont), 48000), new SoundChannel(SoftwareMixer, "General MIDI"));
        }
        MidiDevice = new Midi(midiMapper, CpuState, configuration.Mt32RomsPath, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(MidiDevice);

        // Services
        // memoryAsmWriter is common to InterruptInstaller and AssemblyRoutineInstaller so that they both write at the same address (Bios Segment F000)
        MemoryAsmWriter memoryAsmWriter = new(Memory, new SegmentedAddress(configuration.ProvidedAsmHandlersSegment, 0), CallbackHandler);
        InterruptInstaller = new InterruptInstaller(new InterruptVectorTable(Memory), memoryAsmWriter, Cpu.FunctionHandler);
        AssemblyRoutineInstaller = new AssemblyRoutineInstaller(memoryAsmWriter, Cpu.FunctionHandler);

        VgaRom = new VgaRom();
        Memory.RegisterMapping(MemoryMap.VideoBiosSegment << 4, VgaRom.Size, VgaRom);
        VgaFunctions = new VgaFunctionality(interruptVectorTable, Memory, IoPortDispatcher, BiosDataArea, VgaRom,  configuration.InitializeDOS is true);
        VideoInt10Handler = new VgaBios(Memory, Cpu, VgaFunctions, BiosDataArea, loggerService);

        TimerInt8Handler = new TimerInt8Handler(Memory, Cpu, DualPic, Timer, BiosDataArea, loggerService);
        BiosKeyboardInt9Handler = new BiosKeyboardInt9Handler(Memory, Cpu, DualPic, Keyboard, BiosDataArea, loggerService);

        BiosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(Memory, Cpu, loggerService);
        SystemBiosInt12Handler = new SystemBiosInt12Handler(Memory, Cpu, BiosDataArea, loggerService);
        SystemBiosInt15Handler = new SystemBiosInt15Handler(Memory, Cpu, Memory.A20Gate, loggerService);
        KeyboardInt16Handler = new KeyboardInt16Handler(Memory, Cpu, loggerService, BiosKeyboardInt9Handler.BiosKeyboardBuffer);

        SystemClockInt1AHandler = new SystemClockInt1AHandler(Memory, Cpu, loggerService, TimerInt8Handler);

        MouseDriver = new MouseDriver(Cpu, Memory, MouseDevice, gui, VgaFunctions, loggerService);
        
        var keyboardStreamedInput = new KeyboardStreamedInput(KeyboardInt16Handler);
        var console = new ConsoleDevice(CpuState, VgaFunctions, keyboardStreamedInput, DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout, "CON", loggerService);
        var stdAux = new CharacterDevice(DeviceAttributes.Character, "AUX", loggerService);
        var printer = new CharacterDevice(DeviceAttributes.Character, "PRN", loggerService);
        var clock = new CharacterDevice(DeviceAttributes.Character | DeviceAttributes.CurrentClock, "CLOCK", loggerService);
        var hdd = new BlockDevice(DeviceAttributes.FatDevice, 1);
        CountryInfo countryInfo = new();
        DosPathResolver dosPathResolver = new(configuration.CDrive, configuration.Exe);
        DosFileManager dosFileManager = new DosFileManager(memory, dosPathResolver, loggerService, printer, stdAux);
        DosMemoryManager dosMemoryManager = new DosMemoryManager(memory, loggerService);
        DosInt20Handler dosInt20Handler = new DosInt20Handler(memory, Cpu, loggerService);
        DosInt21Handler dosInt21Handler = new DosInt21Handler(
            memory, Cpu, interruptVectorTable, countryInfo, stdAux, printer, console, clock, hdd, dosMemoryManager,
            dosFileManager, KeyboardInt16Handler, VgaFunctions, loggerService);
        DosInt2fHandler dosInt2FHandler = new DosInt2fHandler(memory, Cpu, loggerService);

        Dos = new Dos(Memory, Cpu, new(),
            console, stdAux, printer, clock, hdd,
            dosFileManager, dosMemoryManager,
            dosInt20Handler, dosInt21Handler, dosInt2FHandler, loggerService);

        if (configuration.InitializeDOS is not false) {
            // Register the interrupt handlers
            RegisterInterruptHandler(VideoInt10Handler);
            RegisterInterruptHandler(TimerInt8Handler);
            RegisterInterruptHandler(BiosKeyboardInt9Handler);
            RegisterInterruptHandler(BiosEquipmentDeterminationInt11Handler);
            RegisterInterruptHandler(SystemBiosInt12Handler);
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
                SoftwareMixer.Dispose();
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

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        Memory.Accept(emulatorDebugger);
        CfgCpu.Accept(emulatorDebugger);
        VgaCard.Accept(emulatorDebugger);
        VgaRenderer.Accept(emulatorDebugger);
        VgaRegisters.Accept(emulatorDebugger);
        MidiDevice.Accept(emulatorDebugger);
        SoftwareMixer.Accept(emulatorDebugger);
        Timer.Accept(emulatorDebugger);
    }
}