namespace Spice86.Tests;

using MeltySynth;

using Mt32emu;

using System;

using NSubstitute;

using Spice86.Core.Backend.Audio.PortAudio;
using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
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
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
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
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Timer = Spice86.Core.Emulator.Devices.Timer.Timer;

public class MachineCreator {
    public ProgramExecutor CreateProgramExecutorFromBinName(string binName, bool recordData = false) {
        return CreateProgramExecutorForBin($"Resources/cpuTests/{binName}.bin", recordData);
    }

    public ProgramExecutor CreateProgramExecutorForBin(string binPath, bool recordData = false) {
        Configuration configuration = new Configuration {
            // making sure int8 is not going to be triggered during the tests
            InstructionsPerSecond = 10000000,
            Exe = binPath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = Array.Empty<byte>(),
            InitializeDOS = false,
            DumpDataOnExit = recordData
        };

        ILoggerService loggerService = Substitute.For<LoggerService>(new LoggerPropertyBag());
        PauseHandler pauseHandler = new(loggerService);
        
        RecordedDataReader reader = new(configuration.RecordedDataDirectory, loggerService);
        ExecutionFlowRecorder executionFlowRecorder = reader.ReadExecutionFlowRecorderFromFileOrCreate(configuration.DumpDataOnExit is not false);
        State cpuState = new();
        IOPortDispatcher ioPortDispatcher = new(cpuState, loggerService, configuration.FailOnUnhandledPort);
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20gate = new(configuration.A20Gate);
        IMemory memory = new Memory(new MemoryBreakpoints(), ram, a20gate);
        MachineBreakpoints machineBreakpoints = new(pauseHandler, new BreakPointHolder(), new BreakPointHolder(), memory, cpuState);
        
        bool initializeResetVector = configuration.InitializeDOS is true;
        if (initializeResetVector) {
            // Put HLT instruction at the reset address
            memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
        var biosDataArea = new BiosDataArea(memory) {
            ConventionalMemorySizeKb = (ushort)Math.Clamp(ram.Size / 1024, 0, 640)
        };
        var dualPic = new DualPic(cpuState,
            configuration.FailOnUnhandledPort, configuration.InitializeDOS is false, loggerService);

        CallbackHandler callbackHandler = new(cpuState, loggerService);

        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, cpuState);
        Alu8 alu8 = new(cpuState);
        Alu16 alu16 = new(cpuState);
        Alu32 alu32 = new(cpuState);
        FunctionHandler functionHandler = new(memory, cpuState, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, cpuState, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        Cpu cpu  = new(interruptVectorTable, alu8, alu16, alu32, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, cpuState,
            dualPic, ioPortDispatcher, callbackHandler, machineBreakpoints,
            loggerService, executionFlowRecorder);
        
        // IO devices
        DmaController dmaController = new(memory, cpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(ioPortDispatcher, dmaController);

        RegisterIoPortHandler(ioPortDispatcher, dualPic);

        VideoState videoState = new();
        VgaIoPortHandler videoInt10Handler = new(cpuState, loggerService, videoState, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, videoInt10Handler);

        IGui? gui = null;
        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(videoState);
        memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        Renderer renderer = new(videoState, vgaMemory);
        VgaCard vgaCard = new(gui, renderer, loggerService);
        Timer timer = new Timer(configuration, cpuState, loggerService, dualPic);
        RegisterIoPortHandler(ioPortDispatcher, timer);
        Keyboard keyboard = new Keyboard(cpuState, a20gate, dualPic, loggerService, gui, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, keyboard);
        Mouse mouse = new Mouse(cpuState, dualPic, gui, configuration.Mouse, loggerService, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, mouse);
        Joystick joystick = new Joystick(cpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(ioPortDispatcher, joystick);
        
        SoftwareMixer softwareMixer = new(new  AudioPlayerFactory(new PortAudioPlayerFactory(loggerService)));
        
        PcSpeaker pcSpeaker = new PcSpeaker(
            new SoundChannel(softwareMixer, nameof(PcSpeaker)), cpuState,
            loggerService, configuration.FailOnUnhandledPort);
        
        RegisterIoPortHandler(ioPortDispatcher, pcSpeaker);
        
        SoundChannel fmSynthSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster OPL3 FM Synth");
        OPL3FM opl3fm = new OPL3FM(fmSynthSoundChannel, cpuState, configuration.FailOnUnhandledPort, loggerService, pauseHandler);
        RegisterIoPortHandler(ioPortDispatcher, opl3fm);
        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.Sb16);
        SoundChannel pcmSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster PCM");
        HardwareMixer hardwareMixer = new HardwareMixer(soundBlasterHardwareConfig, pcmSoundChannel, fmSynthSoundChannel, loggerService);
        DmaChannel eightByteDmaChannel = dmaController.Channels[soundBlasterHardwareConfig.LowDma];
        Dsp dsp = new Dsp(eightByteDmaChannel, dmaController.Channels[soundBlasterHardwareConfig.HighDma]);
        SoundBlaster soundBlaster = new SoundBlaster(
            pcmSoundChannel, hardwareMixer, dsp, eightByteDmaChannel, fmSynthSoundChannel, cpuState, dmaController, dualPic, configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);
        RegisterIoPortHandler(ioPortDispatcher, soundBlaster);
        
        GravisUltraSound gravisUltraSound = new GravisUltraSound(cpuState, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(ioPortDispatcher, gravisUltraSound);
        
        // the external MIDI device (external General MIDI or external Roland MT-32).
        MidiDevice midiMapper;
        if (!string.IsNullOrWhiteSpace(configuration.Mt32RomsPath) && File.Exists(configuration.Mt32RomsPath)) {
            midiMapper = new Mt32MidiDevice(new Mt32Context(), new SoundChannel(softwareMixer, "MT-32"), configuration.Mt32RomsPath, loggerService);
        } else {
            midiMapper = new GeneralMidiDevice(
                new Synthesizer(new SoundFont(GeneralMidiDevice.SoundFont), 48000),
                new SoundChannel(softwareMixer, "General MIDI"),
                loggerService,
                pauseHandler);
        }
        Midi midiDevice = new Midi(midiMapper, cpuState, configuration.Mt32RomsPath, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(ioPortDispatcher, midiDevice);

        // Services
        // memoryAsmWriter is common to InterruptInstaller and AssemblyRoutineInstaller so that they both write at the same address (Bios Segment F000)
        MemoryAsmWriter memoryAsmWriter = new(memory, new SegmentedAddress(configuration.ProvidedAsmHandlersSegment, 0), callbackHandler);
        InterruptInstaller interruptInstaller = new InterruptInstaller(interruptVectorTable, memoryAsmWriter, cpu.FunctionHandler);
        AssemblyRoutineInstaller assemblyRoutineInstaller = new AssemblyRoutineInstaller(memoryAsmWriter, cpu.FunctionHandler);

        VgaRom vgaRom = new VgaRom();
        memory.RegisterMapping(MemoryMap.VideoBiosSegment << 4, vgaRom.Size, vgaRom);
        VgaFunctionality vgaFunctionality = new VgaFunctionality(interruptVectorTable, memory, ioPortDispatcher, biosDataArea, vgaRom,  configuration.InitializeDOS is true);
        VgaBios vgaBios = new VgaBios(memory, cpu, vgaFunctionality, biosDataArea, loggerService);

        TimerInt8Handler timerInt8Handler = new TimerInt8Handler(memory, cpu, dualPic, timer, biosDataArea, loggerService);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new BiosKeyboardInt9Handler(memory, cpu, dualPic, keyboard, biosDataArea, loggerService);

        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(memory, cpu, loggerService);
        SystemBiosInt12Handler systemBiosInt12Handler = new SystemBiosInt12Handler(memory, cpu, biosDataArea, loggerService);
        SystemBiosInt15Handler systemBiosInt15Handler = new SystemBiosInt15Handler(memory, cpu, a20gate, loggerService);
        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(memory, cpu, loggerService, biosKeyboardInt9Handler.BiosKeyboardBuffer);

        SystemClockInt1AHandler systemClockInt1AHandler = new SystemClockInt1AHandler(memory, cpu, loggerService, timerInt8Handler);

        MouseDriver mouseDriver = new MouseDriver(cpu, memory, mouse, gui, vgaFunctionality, loggerService);
        
        var keyboardStreamedInput = new KeyboardStreamedInput(keyboardInt16Handler);
        var console = new ConsoleDevice(cpuState, vgaFunctionality, keyboardStreamedInput, DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout, "CON", loggerService);
        var stdAux = new CharacterDevice(DeviceAttributes.Character, "AUX", loggerService);
        var printer = new CharacterDevice(DeviceAttributes.Character, "PRN", loggerService);
        var clock = new CharacterDevice(DeviceAttributes.Character | DeviceAttributes.CurrentClock, "CLOCK", loggerService);
        var hdd = new BlockDevice(DeviceAttributes.FatDevice, 1);
        CountryInfo countryInfo = new();
        DosPathResolver dosPathResolver = new(configuration.CDrive, configuration.Exe);
        DosFileManager dosFileManager = new DosFileManager(memory, dosPathResolver, loggerService, printer, stdAux);
        DosMemoryManager dosMemoryManager = new DosMemoryManager(memory, loggerService);
        DosInt20Handler dosInt20Handler = new DosInt20Handler(memory, cpu, loggerService);
        DosInt21Handler dosInt21Handler = new DosInt21Handler(
            memory, cpu, interruptVectorTable, countryInfo, stdAux, printer, console, clock, hdd, dosMemoryManager,
            dosFileManager, keyboardInt16Handler, vgaFunctionality, loggerService);
        DosInt2fHandler dosInt2FHandler = new DosInt2fHandler(memory, cpu, loggerService);
        Core.Emulator.OperatingSystem.Dos dos = new(memory, cpu, new(),
            console, stdAux, printer, clock, hdd,
            new Dictionary<string, string>() { { "BLASTER", soundBlaster.BlasterString } },
            configuration.Ems, configuration.InitializeDOS is not false,
            dosFileManager, dosMemoryManager, dosInt20Handler, dosInt21Handler, dosInt2FHandler,
            loggerService);
        
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
            
            var mouseInt33Handler = new MouseInt33Handler(memory, cpu, loggerService, mouseDriver);
            RegisterInterruptHandler(interruptInstaller, mouseInt33Handler);

            var mouseIrq12Handler = new BiosMouseInt74Handler(dualPic, memory);
            RegisterInterruptHandler(interruptInstaller, mouseIrq12Handler);

            SegmentedAddress mouseDriverAddress = assemblyRoutineInstaller.InstallAssemblyRoutine(mouseDriver);
            mouseIrq12Handler.SetMouseDriverAddress(mouseDriverAddress);
        }
        
        InstructionFieldValueRetriever instructionFieldValueRetriever = new(memory);
        ModRmExecutor modRmExecutor = new(cpuState, memory, instructionFieldValueRetriever);
        InstructionExecutionHelper instructionExecutionHelper = new(
            cpuState, memory, ioPortDispatcher,
            callbackHandler, interruptVectorTable, stack,
            alu8, alu16, alu32,
            instructionFieldValueRetriever, modRmExecutor, loggerService);
        ExecutionContextManager executionContextManager = new(machineBreakpoints);
        NodeLinker nodeLinker = new();
        InstructionsFeeder instructionsFeeder = new(new CurrentInstructions(memory, machineBreakpoints), new InstructionParser(memory, cpuState), new PreviousInstructions(memory));
        CfgNodeFeeder cfgNodeFeeder = new(instructionsFeeder, new([nodeLinker, instructionsFeeder]), nodeLinker, cpuState);
        Core.Emulator.CPU.CfgCpu.CfgCpu cfgCpu = new(instructionExecutionHelper, executionContextManager, cfgNodeFeeder, cpuState, dualPic);
        
        Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler, biosKeyboardInt9Handler,
            callbackHandler, interruptInstaller,
            assemblyRoutineInstaller, cpu,
            cfgCpu, cpuState, dos, gravisUltraSound, ioPortDispatcher,
            joystick, keyboard, keyboardInt16Handler, machineBreakpoints, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler, systemBiosInt15Handler, systemClockInt1AHandler, timer,
            timerInt8Handler,
            vgaCard, videoState, ioPortDispatcher, renderer, vgaBios, vgaRom,
            dmaController, opl3fm, softwareMixer, mouse, mouseDriver,
            vgaFunctionality);
        
        InitializeFunctionHandlers(configuration, machine,  loggerService, reader.ReadGhidraSymbolsFromFileOrCreate(), functionHandler, functionHandlerInExternalInterrupt);
        RecorderDataWriter recorderDataWriter = new(executionFlowRecorder,
            cpuState,
            new MemoryDataExporter(memory, callbackHandler, configuration,
                configuration.RecordedDataDirectory, loggerService),
            new ExecutionFlowDumper(loggerService),
            loggerService,
            configuration.RecordedDataDirectory);
        
        ProgramExecutor programExecutor = new(configuration, loggerService, recorderDataWriter,
            machineBreakpoints, machine, dos, callbackHandler, functionHandler, executionFlowRecorder,
            pauseHandler);
        cpu.ErrorOnUninitializedInterruptHandler = false;
        cpuState.Flags.IsDOSBoxCompatible = false;
        return programExecutor;
    }
    
    private static Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILoggerService loggerService, Configuration configuration, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (configuration.OverrideSupplier == null) {
            return res;
        }

        if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            loggerService.Verbose("Override supplied: {OverrideSupplier}", configuration.OverrideSupplier);
        }

        foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in configuration.OverrideSupplier
                .GenerateFunctionInformations(loggerService, configuration, configuration.ProgramEntryPointSegment, machine)) {
            res.Add(element.Key, element.Value);
        }

        return res;
    }
    
    private static void RegisterInterruptHandler(InterruptInstaller interruptInstaller, IInterruptHandler interruptHandler) => interruptInstaller.InstallInterruptHandler(interruptHandler);
    private static void RegisterIoPortHandler(IOPortDispatcher ioPortDispatcher, IIOPortHandler ioPortHandler) => ioPortHandler.InitPortHandlers(ioPortDispatcher);

    private static void InitializeFunctionHandlers(Configuration configuration, Machine machine, ILoggerService loggerService,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, FunctionHandler cpuFunctionHandler, FunctionHandler cpuFunctionHandlerInExternalInterrupt) {
        if (configuration.OverrideSupplier != null) {
            DictionaryUtils.AddAll(functionInformations,
                GenerateFunctionInformations(loggerService, configuration,
                    machine));
        }

        if (functionInformations.Count == 0) {
            return;
        }

        bool useCodeOverride = configuration.UseCodeOverrideOption;
        SetupFunctionHandler(cpuFunctionHandler, functionInformations, useCodeOverride);
        SetupFunctionHandler(cpuFunctionHandlerInExternalInterrupt, functionInformations, useCodeOverride);
    }
    
    private static void SetupFunctionHandler(FunctionHandler functionHandler,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, bool useCodeOverride) {
        functionHandler.FunctionInformations = functionInformations;
        functionHandler.UseCodeOverride = useCodeOverride;
    }

}