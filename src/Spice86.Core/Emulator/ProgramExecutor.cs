namespace Spice86.Core.Emulator;

using Function.Dump;

using MeltySynth;

using Mt32emu;

using Spice86.Core.Backend.Audio.PortAudio;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.Registers;
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
using Spice86.Core.Emulator.Gdb;
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
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.LoadableFile.Bios;
using Spice86.Core.Emulator.LoadableFile.Dos.Com;
using Spice86.Core.Emulator.LoadableFile.Dos.Exe;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Security.Cryptography;

using GeneralRegisters = Spice86.Core.Emulator.CPU.Registers.GeneralRegisters;

/// <inheritdoc cref="IProgramExecutor"/>
public sealed class ProgramExecutor : IProgramExecutor {
    private readonly ILoggerService _loggerService;
    private bool _disposed;
    private readonly Configuration _configuration;
    private readonly GdbServer? _gdbServer;
    private readonly EmulationLoop _emulationLoop;
    private readonly RecorderDataWriter _recorderDataWriter;
    private readonly IMemory _memory;
    private readonly State _cpuState;
    private readonly CallbackHandler _callbackHandler;
    private readonly FunctionHandler _functionHandler;
    private readonly ExecutionFlowRecorder _executionFlowRecorder;
    private GdbCommandHandler? _gdbCommandHandler;

    /// <summary>
    /// Initializes a new instance of <see cref="ProgramExecutor"/>
    /// </summary>
    /// <param name="configuration">The emulator <see cref="Configuration"/> to use.</param>
    /// <param name="loggerService">The logging service to use. Provided via DI.</param>
    /// <param name="gui">The GUI to use for user actions. Can be null for headless mode or unit tests.</param>
    public ProgramExecutor(Configuration configuration, ILoggerService loggerService, IGui? gui) {
        _configuration = configuration;
        _loggerService = loggerService;
        PauseHandler pauseHandler = new(_loggerService);
        RecordedDataReader reader = new(_configuration.RecordedDataDirectory, _loggerService);
        ExecutionFlowRecorder executionFlowRecorder = reader.ReadExecutionFlowRecorderFromFileOrCreate(_configuration.DumpDataOnExit is not false);
        State cpuState = new(new Flags(), new GeneralRegisters(), new SegmentRegisters());
        IOPortDispatcher ioPortDispatcher = new(cpuState, _loggerService, _configuration.FailOnUnhandledPort);
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20gate = new(configuration.A20Gate);
        IMemory memory = new Memory.Memory(new MemoryBreakpoints(), ram, a20gate);
        MachineBreakpoints machineBreakpoints = new(pauseHandler, new BreakPointHolder(), new BreakPointHolder(), memory, cpuState);
        
        bool initializeResetVector = configuration.InitializeDOS is true;
        if (initializeResetVector) {
            // Put HLT instruction at the reset address
            memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
        var biosDataArea = new BiosDataArea(memory) {
            ConventionalMemorySizeKb = (ushort)Math.Clamp(ram.Size / 1024, 0, 640)
        };
        var dualPic = new DualPic(new Pic(_loggerService), new Pic(_loggerService), cpuState,
            configuration.FailOnUnhandledPort, configuration.InitializeDOS is false, _loggerService);

        CallbackHandler callbackHandler = new(cpuState, _loggerService);

        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, cpuState);
        Alu8 alu8 = new(cpuState);
        Alu16 alu16 = new(cpuState);
        Alu32 alu32 = new(cpuState);
        FunctionHandler functionHandler = new(memory, cpuState, executionFlowRecorder, _loggerService, configuration.DumpDataOnExit is not false);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, cpuState, executionFlowRecorder, _loggerService, configuration.DumpDataOnExit is not false);
        Cpu cpu  = new(interruptVectorTable, alu8, alu16, alu32, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, cpuState,
            dualPic, ioPortDispatcher, callbackHandler, machineBreakpoints,
            _loggerService, executionFlowRecorder);
        
        InstructionFieldValueRetriever instructionFieldValueRetriever = new(memory);
        ModRmExecutor modRmExecutor = new(cpuState, memory, instructionFieldValueRetriever);
        InstructionExecutionHelper instructionExecutionHelper = new(
            cpuState, memory, ioPortDispatcher,
            callbackHandler, interruptVectorTable, stack,
            alu8, alu16, alu32,
            instructionFieldValueRetriever, modRmExecutor, _loggerService);
        ExecutionContextManager executionContextManager = new(machineBreakpoints);
        NodeLinker nodeLinker = new();
        InstructionsFeeder instructionsFeeder = new(new CurrentInstructions(memory, machineBreakpoints), new InstructionParser(memory, cpuState), new PreviousInstructions(memory));
        CfgNodeFeeder cfgNodeFeeder = new(instructionsFeeder, new([nodeLinker, instructionsFeeder]), nodeLinker, cpuState);
        CfgCpu cfgCpu = new(instructionExecutionHelper, executionContextManager, cfgNodeFeeder, cpuState, dualPic);

        // IO devices
        DmaController dmaController = new(memory, cpuState, configuration.FailOnUnhandledPort, _loggerService);
        RegisterIoPortHandler(ioPortDispatcher, dmaController);

        RegisterIoPortHandler(ioPortDispatcher, dualPic);

        DacRegisters dacRegisters = new(new ArgbPalette());
        VideoState videoState = new(dacRegisters, new(
                new(),new(),new()),
                new(new(), new(), new(), new(), new()),
                new(new(), new(), new(), new(), new(), new(), new(), new(), new(), new()),
                new(new(), new(), new(), new(), new(), new()),
                new(new(), new(), new()));
        VgaIoPortHandler videoInt10Handler = new(cpuState, _loggerService, videoState, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, videoInt10Handler);

        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(videoState);
        memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        Renderer renderer = new(videoState, vgaMemory);
        VgaCard vgaCard = new(gui, renderer, _loggerService);
        
        Counter firstCounter = new Counter(cpuState, _loggerService, CreateCounterActivator(cpuState, _loggerService, configuration)) {
            Index = 0
        };
        Counter secondCounter = new Counter(cpuState, _loggerService, CreateCounterActivator(cpuState, _loggerService, configuration)) {
            Index = 1
        };
        Counter thirdCounter = new Counter(cpuState, _loggerService, CreateCounterActivator(cpuState, _loggerService, configuration)) {
            Index = 2
        };
        
        Timer timer = new Timer(cpuState, _loggerService, dualPic, firstCounter, secondCounter, thirdCounter, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, timer);
        Keyboard keyboard = new Keyboard(cpuState, a20gate, dualPic, _loggerService, gui, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, keyboard);
        Mouse mouse = new Mouse(cpuState, dualPic, gui, configuration.Mouse, _loggerService, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, mouse);
        Joystick joystick = new Joystick(cpuState, configuration.FailOnUnhandledPort, _loggerService);
        RegisterIoPortHandler(ioPortDispatcher, joystick);
        
        SoftwareMixer softwareMixer = new(new  AudioPlayerFactory(new PortAudioPlayerFactory(_loggerService)));
        
        PcSpeaker pcSpeaker = new PcSpeaker(
            new LatchedUInt16(),
            new SoundChannel(softwareMixer, nameof(PcSpeaker)), cpuState,
            _loggerService, configuration.FailOnUnhandledPort);
        
        RegisterIoPortHandler(ioPortDispatcher, pcSpeaker);
        
        SoundChannel fmSynthSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster OPL3 FM Synth");
        OPL3FM opl3fm = new OPL3FM(new FmSynthesizer(48000), fmSynthSoundChannel, cpuState, configuration.FailOnUnhandledPort, _loggerService);
        RegisterIoPortHandler(ioPortDispatcher, opl3fm);
        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.Sb16);
        SoundChannel pcmSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster PCM");
        HardwareMixer hardwareMixer = new HardwareMixer(soundBlasterHardwareConfig, pcmSoundChannel, fmSynthSoundChannel, _loggerService);
        DmaChannel eightByteDmaChannel = dmaController.Channels[soundBlasterHardwareConfig.LowDma];
        Dsp dsp = new Dsp(eightByteDmaChannel, dmaController.Channels[soundBlasterHardwareConfig.HighDma], new ADPCM2(),  new ADPCM3(), new ADPCM4());
        SoundBlaster soundBlaster = new SoundBlaster(pcmSoundChannel, hardwareMixer, dsp, eightByteDmaChannel, fmSynthSoundChannel, cpuState, dmaController, dualPic, configuration.FailOnUnhandledPort, _loggerService, soundBlasterHardwareConfig);
        RegisterIoPortHandler(ioPortDispatcher, soundBlaster);
        
        GravisUltraSound gravisUltraSound = new GravisUltraSound(cpuState, configuration.FailOnUnhandledPort, _loggerService);
        RegisterIoPortHandler(ioPortDispatcher, gravisUltraSound);
        
        // the external MIDI device (external General MIDI or external Roland MT-32).
        MidiDevice midiMapper;
        if (!string.IsNullOrWhiteSpace(configuration.Mt32RomsPath) && File.Exists(configuration.Mt32RomsPath)) {
            midiMapper = new Mt32MidiDevice(new Mt32Context(), new SoundChannel(softwareMixer, "MT-32"), configuration.Mt32RomsPath, _loggerService);
        } else {
            midiMapper = new GeneralMidiDevice(new Synthesizer(new SoundFont(GeneralMidiDevice.SoundFont), 48000), new SoundChannel(softwareMixer, "General MIDI"));
        }
        Midi midiDevice = new Midi(midiMapper, cpuState, configuration.Mt32RomsPath, configuration.FailOnUnhandledPort, _loggerService);
        RegisterIoPortHandler(ioPortDispatcher, midiDevice);

        // Services
        // memoryAsmWriter is common to InterruptInstaller and AssemblyRoutineInstaller so that they both write at the same address (Bios Segment F000)
        MemoryAsmWriter memoryAsmWriter = new(memory, new SegmentedAddress(configuration.ProvidedAsmHandlersSegment, 0), callbackHandler);
        InterruptInstaller interruptInstaller = new InterruptInstaller(new InterruptVectorTable(memory), memoryAsmWriter, cpu.FunctionHandler);
        AssemblyRoutineInstaller assemblyRoutineInstaller = new AssemblyRoutineInstaller(memoryAsmWriter, cpu.FunctionHandler);

        VgaRom vgaRom = new VgaRom();
        memory.RegisterMapping(MemoryMap.VideoBiosSegment << 4, vgaRom.Size, vgaRom);
        VgaFunctionality vgaFunctionality = new VgaFunctionality(interruptVectorTable, memory, ioPortDispatcher, biosDataArea, vgaRom,  configuration.InitializeDOS is true);
        VgaBios vgaBios = new VgaBios(memory, cpu, vgaFunctionality, biosDataArea, _loggerService);

        TimerInt8Handler timerInt8Handler = new TimerInt8Handler(memory, cpu, dualPic, timer, biosDataArea, _loggerService);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new BiosKeyboardInt9Handler(memory, cpu, dualPic, keyboard, biosDataArea, _loggerService);

        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(memory, cpu, _loggerService);
        SystemBiosInt12Handler systemBiosInt12Handler = new SystemBiosInt12Handler(memory, cpu, biosDataArea, _loggerService);
        SystemBiosInt15Handler systemBiosInt15Handler = new SystemBiosInt15Handler(memory, cpu, a20gate, _loggerService);
        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(memory, cpu, _loggerService, biosKeyboardInt9Handler.BiosKeyboardBuffer);

        SystemClockInt1AHandler systemClockInt1AHandler = new SystemClockInt1AHandler(memory, cpu, _loggerService, timerInt8Handler);

        MouseDriver mouseDriver = new MouseDriver(cpu, memory, mouse, gui, vgaFunctionality, _loggerService);
        
        var keyboardStreamedInput = new KeyboardStreamedInput(keyboardInt16Handler);
        var console = new ConsoleDevice(cpuState, vgaFunctionality, keyboardStreamedInput, DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout, "CON", _loggerService);
        var stdAux = new CharacterDevice(DeviceAttributes.Character, "AUX", _loggerService);
        var printer = new CharacterDevice(DeviceAttributes.Character, "PRN", _loggerService);
        var clock = new CharacterDevice(DeviceAttributes.Character | DeviceAttributes.CurrentClock, "CLOCK", _loggerService);
        var hdd = new BlockDevice(DeviceAttributes.FatDevice, 1);
        CountryInfo countryInfo = new();
        DosPathResolver dosPathResolver = new(configuration.CDrive, configuration.Exe);
        DosFileManager dosFileManager = new DosFileManager(memory, dosPathResolver, _loggerService, printer, stdAux);
        DosMemoryManager dosMemoryManager = new DosMemoryManager(memory, _loggerService);
        DosInt20Handler dosInt20Handler = new DosInt20Handler(memory, cpu, _loggerService);
        DosInt21Handler dosInt21Handler = new DosInt21Handler(
            memory, cpu, interruptVectorTable, countryInfo, stdAux, printer, console, clock, hdd, dosMemoryManager,
            dosFileManager, keyboardInt16Handler, vgaFunctionality, _loggerService);
        DosInt2fHandler dosInt2FHandler = new DosInt2fHandler(memory, cpu, _loggerService);
        Dos dos = new Dos(memory, cpu, new(),
            console, stdAux, printer, clock, hdd,
            new Dictionary<string, string>() { { "BLASTER", soundBlaster.BlasterString } },
            configuration.Ems, configuration.InitializeDOS is not false,
            dosFileManager, dosMemoryManager, dosInt20Handler, dosInt21Handler, dosInt2FHandler,
            _loggerService);
        
        if (configuration.InitializeDOS is not false) {
            // Register the interrupt handlers
            RegisterInterruptHandler(interruptInstaller, vgaBios);
            RegisterInterruptHandler(interruptInstaller, timerInt8Handler);
            RegisterInterruptHandler(interruptInstaller, biosKeyboardInt9Handler);
            RegisterInterruptHandler(interruptInstaller, biosEquipmentDeterminationInt11Handler);
            RegisterInterruptHandler(interruptInstaller, systemBiosInt12Handler);
            RegisterInterruptHandler(interruptInstaller, systemBiosInt15Handler);
            RegisterInterruptHandler(interruptInstaller, keyboardInt16Handler);
            RegisterInterruptHandler(interruptInstaller, systemClockInt1AHandler);
            RegisterInterruptHandler(interruptInstaller, dosInt20Handler);
            RegisterInterruptHandler(interruptInstaller, dosInt21Handler);
            RegisterInterruptHandler(interruptInstaller, dosInt2FHandler);
            
            var mouseInt33Handler = new MouseInt33Handler(memory, cpu, _loggerService, mouseDriver);
            RegisterInterruptHandler(interruptInstaller, mouseInt33Handler);

            var mouseIrq12Handler = new BiosMouseInt74Handler(dualPic, memory);
            RegisterInterruptHandler(interruptInstaller, mouseIrq12Handler);

            SegmentedAddress mouseDriverAddress = assemblyRoutineInstaller.InstallAssemblyRoutine(mouseDriver);
            mouseIrq12Handler.SetMouseDriverAddress(mouseDriverAddress);
        }
        Machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler, biosKeyboardInt9Handler,
            callbackHandler, interruptInstaller,
            assemblyRoutineInstaller, cpu,
            cfgCpu, cpuState, dos, gravisUltraSound, ioPortDispatcher,
            joystick, keyboard, keyboardInt16Handler, machineBreakpoints, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler, systemBiosInt15Handler, systemClockInt1AHandler, timer,
            timerInt8Handler,
            vgaCard, videoState, ioPortDispatcher, renderer, vgaBios, vgaRom,
            dmaController, opl3fm, softwareMixer, mouse, mouseDriver,
            vgaFunctionality);

        ExecutableFileLoader loader = CreateExecutableFileLoader(_configuration, memory, cpuState, dos.EnvironmentVariables, dosFileManager, dosMemoryManager);
        if (_configuration.InitializeDOS is null) {
            _configuration.InitializeDOS = loader.DosInitializationNeeded;
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("InitializeDOS parameter not provided. Guessed value is: {InitializeDOS}", _configuration.InitializeDOS);
            }
        }
        InitializeFunctionHandlers(_configuration, reader.ReadGhidraSymbolsFromFileOrCreate(), functionHandler, functionHandlerInExternalInterrupt);
        LoadFileToRun(_configuration, loader);
        _recorderDataWriter = new RecorderDataWriter(executionFlowRecorder,
            cpuState,
            new MemoryDataExporter(memory, callbackHandler, _configuration,
                _configuration.RecordedDataDirectory, _loggerService),
            new ExecutionFlowDumper(_loggerService),
            _loggerService,
            _configuration.RecordedDataDirectory);
        _gdbServer = CreateGdbServer(pauseHandler, cpuState, memory, cpu, machineBreakpoints, executionFlowRecorder, functionHandler);
        _emulationLoop = new(loggerService,functionHandler, cpu, cpuState, timer, machineBreakpoints, dmaController, _gdbCommandHandler);
        _memory = memory;
        _cpuState = cpuState;
        _callbackHandler = callbackHandler;
        _functionHandler = functionHandler;
        _executionFlowRecorder = executionFlowRecorder;
    }

    /// <summary>
    /// The emulator machine.
    /// </summary>
    public Machine Machine { get; }

    /// <inheritdoc/>
    public void Run() {
        _gdbServer?.StartServerAndWait();
        _emulationLoop.Run();
        if (_configuration.DumpDataOnExit is not false) {
            DumpEmulatorStateToDirectory(_configuration.RecordedDataDirectory);
        }
    }
    
    /// <summary>
    /// Steps a single instruction for the internal UI debugger
    /// </summary>
    /// <remarks>Depends on the presence of the GDBServer and GDBCommandHandler</remarks>
    public void StepInstruction() {
        _gdbServer?.StepInstruction();
        IsPaused = false;
    }

    /// <inheritdoc/>
    public void DumpEmulatorStateToDirectory(string path) {
        new RecorderDataWriter(_executionFlowRecorder,
                _cpuState,
                new MemoryDataExporter(_memory, _callbackHandler, _configuration, path, _loggerService),
                new ExecutionFlowDumper(_loggerService),
                _loggerService,
                path)
            .DumpAll(_executionFlowRecorder, _functionHandler);
    }

    /// <inheritdoc/>
    public bool IsPaused { get => _emulationLoop.IsPaused; set => _emulationLoop.IsPaused = value; }

    private static void CheckSha256Checksum(byte[] file, byte[]? expectedHash) {
        ArgumentNullException.ThrowIfNull(expectedHash, nameof(expectedHash));
        if (expectedHash.Length == 0) {
            // No hash check
            return;
        }

        byte[] actualHash = SHA256.HashData(file);

        if (!actualHash.AsSpan().SequenceEqual(expectedHash)) {
            string error =
                $"File does not match the expected SHA256 checksum, cannot execute it.\nExpected checksum is {ConvertUtils.ByteArrayToHexString(expectedHash)}.\nGot {ConvertUtils.ByteArrayToHexString(actualHash)}\n";
            throw new UnrecoverableException(error);
        }
    }

    private ExecutableFileLoader CreateExecutableFileLoader(Configuration configuration, IMemory memory, State cpuState, EnvironmentVariables environmentVariables,
        DosFileManager fileManager, DosMemoryManager memoryManager) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        string lowerCaseFileName = executableFileName.ToLowerInvariant();
        ushort entryPointSegment = configuration.ProgramEntryPointSegment;
        if (lowerCaseFileName.EndsWith(".exe")) {
            return new ExeLoader(memory,
                cpuState,
                _loggerService,
                environmentVariables,
                fileManager,
                memoryManager,
                entryPointSegment);
        }

        if (lowerCaseFileName.EndsWith(".com")) {
            return new ComLoader(memory,
                cpuState,
                _loggerService,
                environmentVariables,
                fileManager,
                memoryManager,
                entryPointSegment);
        }

        return new BiosLoader(memory, cpuState, _loggerService);
    }
    
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
    
    private static void RegisterInterruptHandler(InterruptInstaller interruptInstaller, IInterruptHandler interruptHandler) => interruptInstaller.InstallInterruptHandler(interruptHandler);
    private static void RegisterIoPortHandler(IOPortDispatcher ioPortDispatcher, IIOPortHandler ioPortHandler) => ioPortHandler.InitPortHandlers(ioPortDispatcher);
    
    private GdbServer? CreateGdbServer(
        PauseHandler pauseHandler, State cpuState, IMemory memory, Cpu cpu,
        MachineBreakpoints machineBreakpoints, ExecutionFlowRecorder executionFlowRecorder, FunctionHandler functionHandler) {
        int? gdbPort = _configuration.GdbPort;
        if (gdbPort == null) {
            return null;
        }
        GdbIo gdbIo = new(gdbPort.Value, _loggerService);
        GdbFormatter gdbFormatter = new();
        var gdbCommandRegisterHandler = new GdbCommandRegisterHandler(cpuState, gdbFormatter, gdbIo, _loggerService);
        var gdbCommandMemoryHandler = new GdbCommandMemoryHandler(memory, gdbFormatter, gdbIo, _loggerService);
        var gdbCommandBreakpointHandler = new GdbCommandBreakpointHandler(machineBreakpoints, pauseHandler, gdbIo, _loggerService);
        var gdbCustomCommandsHandler = new GdbCustomCommandsHandler(memory, cpuState, cpu,
            Machine.MachineBreakpoints, _recorderDataWriter, gdbIo,
            _loggerService,
            gdbCommandBreakpointHandler.OnBreakPointReached);
        _gdbCommandHandler = new(
            gdbCommandBreakpointHandler, gdbCommandMemoryHandler, gdbCommandRegisterHandler,
            gdbCustomCommandsHandler,
            cpuState,
            pauseHandler,
            executionFlowRecorder,
            functionHandler,
            gdbIo,
            _loggerService);
        return new GdbServer(
            gdbIo,
            cpuState,
            pauseHandler,
            _gdbCommandHandler,
            _loggerService,
            _configuration);
    }

    private Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        IOverrideSupplier? supplier, ushort entryPointSegment, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (supplier == null) {
            return res;
        }

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Override supplied: {OverrideSupplier}", supplier);
        }

        foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in supplier
                    .GenerateFunctionInformations(_loggerService, _configuration, entryPointSegment, machine)) {
            res.Add(element.Key, element.Value);
        }

        return res;
    }

    private void InitializeFunctionHandlers(Configuration configuration,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, FunctionHandler cpuFunctionHandler, FunctionHandler cpuFunctionHandlerInExternalInterrupt) {
        if (configuration.OverrideSupplier != null) {
            DictionaryUtils.AddAll(functionInformations,
                GenerateFunctionInformations(configuration.OverrideSupplier, configuration.ProgramEntryPointSegment,
                    Machine));
        }

        if (functionInformations.Count == 0) {
            return;
        }

        bool useCodeOverride = configuration.UseCodeOverrideOption;
        SetupFunctionHandler(cpuFunctionHandler, functionInformations, useCodeOverride);
        SetupFunctionHandler(cpuFunctionHandlerInExternalInterrupt, functionInformations, useCodeOverride);
    }

    private void LoadFileToRun(Configuration configuration, ExecutableFileLoader loader) {
        string? executableFileName = configuration.Exe;
        ArgumentException.ThrowIfNullOrEmpty(executableFileName);

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading file {FileName} with loader {LoaderType}", executableFileName,
                loader.GetType());
        }

        try {
            byte[] fileContent = loader.LoadFile(executableFileName, configuration.ExeArgs);
            CheckSha256Checksum(fileContent, configuration.ExpectedChecksumValue);
        } catch (IOException e) {
            throw new UnrecoverableException($"Failed to read file {executableFileName}", e);
        }
    }

    private static void SetupFunctionHandler(FunctionHandler functionHandler,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.FunctionInformations = functionInformations;
        functionHandler.UseCodeOverride = useCodeOverride;
    }
    
    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _gdbServer?.Dispose();
                _emulationLoop.Exit();
                Machine.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
        Machine.Accept(emulatorDebugger);
    }
}