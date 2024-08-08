﻿namespace Spice86;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using MeltySynth;

using Microsoft.Extensions.DependencyInjection;

using Mt32emu;

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
using Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
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
using Spice86.Core.Emulator.Devices.Sound.Midi.MT32;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.DependencyInjection;
using Spice86.Infrastructure;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.Views;

/// <summary>
/// Entry point for Spice86 application.
/// </summary>
public class Program {
    /// <summary>
    /// Alternate entry point to use when injecting a class that defines C# overrides of the x86 assembly code found in the target DOS program.
    /// </summary>
    /// <typeparam name="T">Type of the class that defines C# overrides of the x86 assembly code.</typeparam>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="expectedChecksum">The expected checksum of the target DOS program.</param>
    [STAThread]
    public static void RunWithOverrides<T>(string[] args, string expectedChecksum) where T : class, new() {
        List<string> argsList = args.ToList();

        // Inject override
        argsList.Add($"--{nameof(Configuration.OverrideSupplierClassName)}={typeof(T).AssemblyQualifiedName}");
        argsList.Add($"--{nameof(Configuration.ExpectedChecksum)}={expectedChecksum}");
        Main(argsList.ToArray());
    }

    /// <summary>
    /// Entry point of the application.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    [STAThread]
    public static void Main(string[] args) {
        IMessenger messenger = WeakReferenceMessenger.Default;
        ILoggerPropertyBag loggerPropertyBag = new LoggerPropertyBag();
        ILoggerService loggerService = new LoggerService(loggerPropertyBag);
        Configuration configuration = new CommandLineParser().ParseCommandLine(args);
        Startup.SetLoggingLevel(loggerService, configuration);
        IPauseHandler pauseHandler = new PauseHandler(loggerService);
        
        RecordedDataReader reader = new(configuration.RecordedDataDirectory, loggerService);
        ExecutionFlowRecorder executionFlowRecorder = reader.ReadExecutionFlowRecorderFromFileOrCreate(configuration.DumpDataOnExit is not false);
        State state = new();
        IOPortDispatcher ioPortDispatcher = new(state, loggerService, configuration.FailOnUnhandledPort);
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20gate = new(configuration.A20Gate);
        IMemory memory = new Memory(new MemoryBreakpoints(), ram, a20gate);
        MachineBreakpoints machineBreakpoints = new(pauseHandler, memory, state);
        
        bool initializeResetVector = configuration.InitializeDOS is true;
        if (initializeResetVector) {
            // Put HLT instruction at the reset address
            memory.UInt16[0xF000, 0xFFF0] = 0xF4;
        }
        var biosDataArea = new BiosDataArea(memory) {
            ConventionalMemorySizeKb = (ushort)Math.Clamp(ram.Size / 1024, 0, 640)
        };
        var dualPic = new DualPic(state,
            configuration.FailOnUnhandledPort, configuration.InitializeDOS is false, loggerService);

        CallbackHandler callbackHandler = new(state, loggerService);

        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, state);
        Alu8 alu8 = new(state);
        Alu16 alu16 = new(state);
        Alu32 alu32 = new(state);
        FunctionHandler functionHandler = new(memory, state, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, state, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        Cpu cpu  = new(interruptVectorTable, alu8, alu16, alu32, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, state,
            dualPic, ioPortDispatcher, callbackHandler, machineBreakpoints,
            loggerService, executionFlowRecorder);
        
        InstructionFieldValueRetriever instructionFieldValueRetriever = new(memory);
        ModRmExecutor modRmExecutor = new(state, memory, instructionFieldValueRetriever);
        InstructionExecutionHelper instructionExecutionHelper = new(
            state, memory, ioPortDispatcher,
            callbackHandler, interruptVectorTable, stack,
            alu8, alu16, alu32,
            instructionFieldValueRetriever, modRmExecutor, loggerService);
        ExecutionContextManager executionContextManager = new(machineBreakpoints);
        NodeLinker nodeLinker = new();
        InstructionsFeeder instructionsFeeder = new(new CurrentInstructions(memory, machineBreakpoints), new InstructionParser(memory, state), new PreviousInstructions(memory));
        CfgNodeFeeder cfgNodeFeeder = new(instructionsFeeder, new([nodeLinker, instructionsFeeder]), nodeLinker, state);
        CfgCpu cfgCpu = new(instructionExecutionHelper, executionContextManager, cfgNodeFeeder, state, dualPic);

        // IO devices
        DmaController dmaController = new(memory, state, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(ioPortDispatcher, dmaController);

        RegisterIoPortHandler(ioPortDispatcher, dualPic);
        
        VideoState videoState = new();
        VgaIoPortHandler videoInt10Handler = new(state, loggerService, videoState, configuration.FailOnUnhandledPort);
        RegisterIoPortHandler(ioPortDispatcher, videoInt10Handler);
        
        SoftwareMixer softwareMixer = new(loggerService);
        
        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(videoState);
        memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        Renderer vgaRenderer = new(videoState, vgaMemory);
        
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
        
        Midi midiDevice = new Midi(midiMapper, state, configuration.Mt32RomsPath, configuration.FailOnUnhandledPort, loggerService);
        RegisterIoPortHandler(ioPortDispatcher, midiDevice);
        
        Timer timer = new Timer(configuration, state, loggerService, dualPic);
        RegisterIoPortHandler(ioPortDispatcher, timer);

        MainWindowViewModel? gui = null;
        ClassicDesktopStyleApplicationLifetime? desktop = null;
        MainWindow? mainWindow = null;
        if (!configuration.HeadlessMode) {
            desktop = CreateDesktopApp();
            UIDispatcher uiDispatcher = new UIDispatcher(Dispatcher.UIThread);
            PerformanceViewModel performanceViewModel = new(state, pauseHandler, uiDispatcher);
            mainWindow = new() {
                PerformanceViewModel = performanceViewModel
            };
            gui = new MainWindowViewModel(
                videoState.DacRegisters.ArgbPalette, timer, state, memory, softwareMixer, midiDevice, vgaRenderer, videoState, executionContextManager,
                messenger, new WindowService(), new AvaloniaKeyScanCodeConverter(),
                uiDispatcher, new HostStorageProvider(mainWindow.StorageProvider), new TextClipboard(mainWindow.Clipboard),
                configuration, loggerService, new StructureViewModelFactory(configuration, loggerService, pauseHandler), pauseHandler);
        }

        using (gui) {
            VgaCard vgaCard = new(gui, vgaRenderer, loggerService);
            Keyboard keyboard = new Keyboard(state, a20gate, dualPic, loggerService, gui, configuration.FailOnUnhandledPort);
            RegisterIoPortHandler(ioPortDispatcher, keyboard);
            Mouse mouse = new Mouse(state, dualPic, gui, configuration.Mouse, loggerService, configuration.FailOnUnhandledPort);
            RegisterIoPortHandler(ioPortDispatcher, mouse);
            Joystick joystick = new Joystick(state, configuration.FailOnUnhandledPort, loggerService);
            RegisterIoPortHandler(ioPortDispatcher, joystick);
            
            PcSpeaker pcSpeaker = new PcSpeaker(
                new SoundChannel(softwareMixer, nameof(PcSpeaker)), state,
                loggerService, configuration.FailOnUnhandledPort);
            
            RegisterIoPortHandler(ioPortDispatcher, pcSpeaker);
            
            SoundChannel fmSynthSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster OPL3 FM Synth");
            OPL3FM opl3fm = new OPL3FM(fmSynthSoundChannel, state, configuration.FailOnUnhandledPort, loggerService, pauseHandler);
            RegisterIoPortHandler(ioPortDispatcher, opl3fm);
            var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.Sb16);
            SoundChannel pcmSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster PCM");
            HardwareMixer hardwareMixer = new HardwareMixer(soundBlasterHardwareConfig, pcmSoundChannel, fmSynthSoundChannel, loggerService);
            DmaChannel eightByteDmaChannel = dmaController.Channels[soundBlasterHardwareConfig.LowDma];
            Dsp dsp = new Dsp(eightByteDmaChannel, dmaController.Channels[soundBlasterHardwareConfig.HighDma]);
            SoundBlaster soundBlaster = new SoundBlaster(
                pcmSoundChannel, hardwareMixer, dsp, eightByteDmaChannel, fmSynthSoundChannel, state, dmaController, dualPic, configuration.FailOnUnhandledPort,
                loggerService, soundBlasterHardwareConfig, pauseHandler);
            RegisterIoPortHandler(ioPortDispatcher, soundBlaster);
            
            GravisUltraSound gravisUltraSound = new GravisUltraSound(state, configuration.FailOnUnhandledPort, loggerService);
            RegisterIoPortHandler(ioPortDispatcher, gravisUltraSound);
            

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
            var console = new ConsoleDevice(state, vgaFunctionality, keyboardStreamedInput, DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout, "CON", loggerService);
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
            Dos dos = new Dos(memory, cpu, new(),
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
            Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler, biosKeyboardInt9Handler,
                callbackHandler, interruptInstaller,
                assemblyRoutineInstaller, cpu,
                cfgCpu, state, dos, gravisUltraSound, ioPortDispatcher,
                joystick, keyboard, keyboardInt16Handler, machineBreakpoints, memory, midiDevice, pcSpeaker,
                dualPic, soundBlaster, systemBiosInt12Handler, systemBiosInt15Handler, systemClockInt1AHandler, timer,
                timerInt8Handler,
                vgaCard, videoState, ioPortDispatcher, vgaRenderer, vgaBios, vgaRom,
                dmaController, opl3fm, softwareMixer, mouse, mouseDriver,
                vgaFunctionality);
            
            InitializeFunctionHandlers(configuration, machine,  loggerService, reader.ReadGhidraSymbolsFromFileOrCreate(), functionHandler, functionHandlerInExternalInterrupt);
            RecorderDataWriter recorderDataWriter = new(executionFlowRecorder,
                state,
                new MemoryDataExporter(memory, callbackHandler, configuration,
                    configuration.RecordedDataDirectory, loggerService),
                new ExecutionFlowDumper(loggerService),
                loggerService,
                configuration.RecordedDataDirectory);
            
            ProgramExecutor programExecutor = new(configuration, loggerService, recorderDataWriter,
                machineBreakpoints, machine, dos, callbackHandler, functionHandler, executionFlowRecorder,
                pauseHandler);
            if (configuration.HeadlessMode) {
                programExecutor.Run();
            } else if (gui != null && mainWindow != null && desktop != null) {
                StartGraphicalUserInterface(programExecutor, desktop, gui, mainWindow, args);
            }
        }
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

    private static void StartGraphicalUserInterface(IProgramExecutor programExecutor, ClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel mainWindowViewModel, MainWindow mainWindow, string[] args) {
        mainWindow.DataContext = mainWindowViewModel;
        desktop.MainWindow = mainWindow;
        mainWindowViewModel.ProgramExecutor = programExecutor;
        desktop.Start(args);
    }

    private static ClassicDesktopStyleApplicationLifetime CreateDesktopApp() {
        AppBuilder appBuilder = BuildAvaloniaApp();
        ClassicDesktopStyleApplicationLifetime desktop = SetupWithClassicDesktopLifetime(appBuilder);
        return desktop;
    }
    
    private static AppBuilder BuildAvaloniaApp() {
        return AppBuilder.Configure(() => new App())
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();
    }

    private static ClassicDesktopStyleApplicationLifetime SetupWithClassicDesktopLifetime(AppBuilder builder) {
        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        builder.SetupWithLifetime(lifetime);
        return lifetime;
    }
}