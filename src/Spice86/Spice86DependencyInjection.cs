namespace Spice86;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
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
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Infrastructure;
using Spice86.Logging;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.Views;

/// <summary>
/// Class responsible for compile-time dependency injection and runtime emulator lifecycle management
/// </summary>
public class Spice86DependencyInjection : IDisposable {
    private readonly Configuration _configuration;
    private readonly ClassicDesktopStyleApplicationLifetime? _desktop;
    private readonly AppBuilder? _appBuilder;
    public Machine Machine { get; }
    public ProgramExecutor ProgramExecutor { get; }
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private bool _disposed;

    public Spice86DependencyInjection(Configuration configuration,
        AppBuilder? appBuilder = null) {
        _appBuilder = appBuilder;
        LoggerService loggerService = new LoggerService();
        SetLoggingLevel(loggerService, configuration);

        IPauseHandler pauseHandler = new PauseHandler(loggerService);

        RecordedDataReader reader = new(configuration.RecordedDataDirectory,
            loggerService);

        ExecutionFlowRecorder executionFlowRecorder =
            reader.ReadExecutionFlowRecorderFromFileOrCreate(
                configuration.DumpDataOnExit is not false);
        State state = new();

        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state);

        IOPortDispatcher ioPortDispatcher = new(
            emulatorBreakpointsManager.IoReadWriteBreakpoints, state,
            loggerService, configuration.FailOnUnhandledPort);
        Ram ram = new(A20Gate.EndOfHighMemoryArea);

        A20Gate a20Gate = new(configuration.A20Gate);

        Memory memory = new(emulatorBreakpointsManager.MemoryReadWriteBreakpoints,
            ram, a20Gate,
            initializeResetVector: configuration.InitializeDOS is true);

        var biosDataArea =
            new BiosDataArea(memory, conventionalMemorySizeKb:
                (ushort)Math.Clamp(ram.Size / 1024, 0, 640));

        var dualPic = new DualPic(state, ioPortDispatcher,
            configuration.FailOnUnhandledPort,
            configuration.InitializeDOS is false, loggerService);

        CallbackHandler callbackHandler = new(state, loggerService);
        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, state);
        FunctionCatalogue functionCatalogue = new FunctionCatalogue(
            reader.ReadGhidraSymbolsFromFileOrCreate());
        FunctionHandler functionHandler = new(memory, state,
            executionFlowRecorder, functionCatalogue, loggerService);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, state,
            executionFlowRecorder, functionCatalogue, loggerService);

        Cpu cpu = new(interruptVectorTable, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, state,
            dualPic, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager,
            loggerService, executionFlowRecorder);

        CfgCpu cfgCpu = new(memory, state, ioPortDispatcher, callbackHandler,
            dualPic, emulatorBreakpointsManager, functionCatalogue, loggerService);

        IInstructionExecutor instructionExecutor = configuration.CfgCpu ? cfgCpu : cpu;
        IFunctionHandlerProvider functionHandlerProvider = configuration.CfgCpu ? cfgCpu : cpu;
        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            IInstructionExecutor cpuType = configuration.CfgCpu ? cfgCpu : cpu;
            loggerService.Information(
                "Execution will be done with @CpuType", cpuType);
        }

        // IO devices
        Timer timer = new Timer(configuration, state, ioPortDispatcher,
            new CounterConfiguratorFactory(configuration,
            state, pauseHandler, loggerService), loggerService, dualPic);
        TimerInt8Handler timerInt8Handler =
            new TimerInt8Handler(memory, functionHandlerProvider, stack, state,
            dualPic, timer, biosDataArea, loggerService);

        DmaController dmaController =
            new(memory, state, ioPortDispatcher,
            configuration.FailOnUnhandledPort, loggerService);

        CreateVideoCardSupportClasses(configuration, loggerService, state, ioPortDispatcher,
            memory, biosDataArea, interruptVectorTable, stack,
            functionHandlerProvider,
            out VideoState videoState,
            out VgaIoPortHandler videoInt10Handler,
            out Renderer vgaRenderer,
            out VgaRom vgaRom,
            out VgaFunctionality vgaFunctionality,
            out VgaBios vgaBios);

        CreateBiosInterruptHandlers(configuration, loggerService, state,
            a20Gate, memory, biosDataArea, stack, functionHandlerProvider,
            timerInt8Handler,
            out BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler,
            out SystemBiosInt12Handler systemBiosInt12Handler,
            out SystemBiosInt15Handler systemBiosInt15Handler,
            out SystemClockInt1AHandler systemClockInt1AHandler);

        MemoryDataExporter memoryDataExporter = new(memory, callbackHandler,
            configuration, configuration.RecordedDataDirectory, loggerService);

        EmulatorStateSerializer emulatorStateSerializer = new(memoryDataExporter, state,
            executionFlowRecorder, functionCatalogue, loggerService);

        CreateMainWindow(configuration, loggerService, pauseHandler, state,
            timer, emulatorStateSerializer, out MainWindowViewModel? mainWindowViewModel,
            out MainWindow? mainWindow, out ClassicDesktopStyleApplicationLifetime? desktop,
            out ITextClipboard? textClipboard, out IHostStorageProvider? hostStorageProvider,
            out IUIDispatcher? uiThreadDispatcher);

        VgaCard vgaCard = new(mainWindowViewModel, vgaRenderer, loggerService);

        CreateInputDevices(configuration, loggerService, state,
            ioPortDispatcher, a20Gate, memory, biosDataArea, dualPic, stack, cpu,
            functionHandlerProvider, vgaFunctionality, mainWindowViewModel,
            out Keyboard keyboard, out BiosKeyboardInt9Handler biosKeyboardInt9Handler,
            out Mouse mouse, out MouseDriver mouseDriver,
            out KeyboardInt16Handler keyboardInt16Handler,
            out Joystick joystick);

        CreateSoundDevices(configuration, loggerService, pauseHandler, state,
            ioPortDispatcher, dualPic, timer, dmaController, out SoftwareMixer softwareMixer,
            out Midi midiDevice, out PcSpeaker pcSpeaker, out SoundBlaster soundBlaster,
            out GravisUltraSound gravisUltraSound);

        Dos dos = CreateDiskOperatingSystem(configuration, loggerService, state,
            memory, stack, functionHandlerProvider, vgaFunctionality,
            keyboardInt16Handler, soundBlaster);

        Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler,
            biosKeyboardInt9Handler,
            callbackHandler, cpu,
            cfgCpu, state, dos, gravisUltraSound, ioPortDispatcher,
            joystick, keyboard, keyboardInt16Handler,
            emulatorBreakpointsManager, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler,
            systemBiosInt15Handler, systemClockInt1AHandler,
            timer,
            timerInt8Handler,
            vgaCard, videoState, videoInt10Handler,
            vgaRenderer, vgaBios, vgaRom,
            dmaController, soundBlaster.Opl3Fm, softwareMixer, mouse, mouseDriver,
            vgaFunctionality, pauseHandler);

        DictionaryUtils.AddAll(functionCatalogue.FunctionInformations,
            ReadFunctionOverrides(configuration, machine, loggerService));

        InitializeFunctionHandlers(configuration, functionHandler,
            functionHandlerInExternalInterrupt);

        ProgramExecutor programExecutor = new(configuration, emulatorBreakpointsManager,
            emulatorStateSerializer, memory, functionHandlerProvider,
            instructionExecutor, memoryDataExporter, state, timer, dos,
            functionHandler, functionCatalogue, executionFlowRecorder, pauseHandler,
            mainWindowViewModel, dmaController, loggerService);

        CreateBiosAndDosInterruptHandlers(configuration, loggerService,
            state, memory, dualPic, callbackHandler, interruptVectorTable,
            stack, functionCatalogue, functionHandlerProvider, timerInt8Handler,
            vgaBios, biosEquipmentDeterminationInt11Handler, systemBiosInt12Handler,
            systemBiosInt15Handler, systemClockInt1AHandler,
            biosKeyboardInt9Handler, mouseDriver, keyboardInt16Handler, dos);

        DebugWindowViewModel? debugWindowViewModel = CreateInternalDebuggerViewModel(configuration,
            loggerService, pauseHandler, state, emulatorBreakpointsManager,
            memory, stack, functionCatalogue, cfgCpu, videoState, vgaRenderer,
            softwareMixer, midiDevice, memoryDataExporter, textClipboard,
            hostStorageProvider, uiThreadDispatcher);

        if (desktop != null && mainWindow != null && uiThreadDispatcher != null) {
            mainWindow.DataContext = mainWindowViewModel;
            desktop.MainWindow = mainWindow;
            mainWindow.Loaded += (_, _) => {
                // DebugWindow is not shown. Therefore, the instance is not used.
                // But with the alternative ctor it will be in the OwnedWindows collection.
                // This is for the ShowInternalDebuggerBehavior.
                _ = new DebugWindow(owner: mainWindow) {
                    DataContext = debugWindowViewModel
                };
            };
        }

        Machine = machine;
        ProgramExecutor = programExecutor;
        _configuration = configuration;
        _desktop = desktop;
        _mainWindowViewModel = mainWindowViewModel;
    }

    private static Dos CreateDiskOperatingSystem(Configuration configuration,
        LoggerService loggerService, State state, Memory memory, Stack stack,
        IFunctionHandlerProvider functionHandlerProvider,
        VgaFunctionality vgaFunctionality,
        KeyboardInt16Handler keyboardInt16Handler, SoundBlaster soundBlaster) {
        return new Dos(memory, functionHandlerProvider, stack, state,
                    keyboardInt16Handler, vgaFunctionality, configuration.CDrive,
                    configuration.Exe,
                    configuration.InitializeDOS is not false,
                    configuration.Ems,
                    new Dictionary<string, string> { { "BLASTER",
                            soundBlaster.BlasterString } },
                    loggerService);
    }

    private void CreateMainWindow(Configuration configuration,
        LoggerService loggerService, IPauseHandler pauseHandler, State state,
        Timer timer, EmulatorStateSerializer emulatorStateSerializer,
        out MainWindowViewModel? mainWindowViewModel,
        out MainWindow? mainWindow,
        out ClassicDesktopStyleApplicationLifetime? desktop,
        out ITextClipboard? textClipboard,
        out IHostStorageProvider? hostStorageProvider,
        out IUIDispatcher? uiThreadDispatcher) {
        mainWindowViewModel = null;
        mainWindow = null;
        desktop = null;
        textClipboard = null;
        hostStorageProvider = null;
        uiThreadDispatcher = null;
        if (!configuration.HeadlessMode && _appBuilder is not null) {
            desktop = CreateDesktopApp(_appBuilder);
            uiThreadDispatcher = new UIDispatcher(Dispatcher.UIThread);
            PerformanceViewModel performanceViewModel = new(state, pauseHandler,
                uiThreadDispatcher);
            mainWindow = new() {
                PerformanceViewModel = performanceViewModel
            };
            textClipboard = new TextClipboard(mainWindow.Clipboard);
            hostStorageProvider = new HostStorageProvider(
                mainWindow.StorageProvider, configuration, emulatorStateSerializer);
            mainWindowViewModel = new MainWindowViewModel(
                timer, state, uiThreadDispatcher, hostStorageProvider,
                textClipboard, configuration,
                loggerService, pauseHandler, performanceViewModel);
        }
    }

    private static void CreateBiosInterruptHandlers(Configuration configuration,
        LoggerService loggerService, State state, A20Gate a20Gate,
        Memory memory, BiosDataArea biosDataArea, Stack stack,
        IFunctionHandlerProvider functionHandlerProvider,
        TimerInt8Handler timerInt8Handler,
        out BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler,
        out SystemBiosInt12Handler systemBiosInt12Handler,
        out SystemBiosInt15Handler systemBiosInt15Handler,
        out SystemClockInt1AHandler systemClockInt1AHandler) {
        biosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(memory,
                    functionHandlerProvider, stack, state, loggerService);
        systemBiosInt12Handler = new SystemBiosInt12Handler(memory, functionHandlerProvider, stack,
                    state, biosDataArea, loggerService);
        systemBiosInt15Handler = new(memory,
                    functionHandlerProvider, stack, state, a20Gate,
                    configuration.InitializeDOS is not false, loggerService);
        systemClockInt1AHandler = new SystemClockInt1AHandler(memory, functionHandlerProvider, stack,
                    state, loggerService, timerInt8Handler);
    }

    private static void CreateVideoCardSupportClasses(Configuration configuration,
        LoggerService loggerService, State state,
        IOPortDispatcher ioPortDispatcher, Memory memory,
        BiosDataArea biosDataArea, InterruptVectorTable interruptVectorTable,
        Stack stack, IFunctionHandlerProvider functionHandlerProvider,
        out VideoState videoState, out VgaIoPortHandler videoInt10Handler,
        out Renderer vgaRenderer, out VgaRom vgaRom,
        out VgaFunctionality vgaFunctionality, out VgaBios vgaBios) {
        videoState = new();
        videoInt10Handler = new(state, ioPortDispatcher, loggerService, videoState,
                    configuration.FailOnUnhandledPort);
        vgaRenderer = new(memory, videoState);
        vgaRom = new();
        vgaFunctionality = new VgaFunctionality(memory,
            interruptVectorTable, ioPortDispatcher,
            biosDataArea, vgaRom,
            bootUpInTextMode: configuration.InitializeDOS is true);
        vgaBios = new VgaBios(memory, functionHandlerProvider, stack,
            state, vgaFunctionality, biosDataArea, loggerService);
    }

    private static void CreateInputDevices(Configuration configuration,
        LoggerService loggerService, State state,
        IOPortDispatcher ioPortDispatcher, A20Gate a20Gate, Memory memory,
        BiosDataArea biosDataArea, DualPic dualPic, Stack stack, Cpu cpu,
        IFunctionHandlerProvider functionHandlerProvider,
        VgaFunctionality vgaFunctionality, MainWindowViewModel? mainWindowViewModel,
        out Keyboard keyboard,
        out BiosKeyboardInt9Handler biosKeyboardInt9Handler,
        out Mouse mouse,
        out MouseDriver mouseDriver,
        out KeyboardInt16Handler keyboardInt16Handler,
        out Joystick joystick) {
        keyboard = new Keyboard(state, ioPortDispatcher, a20Gate, dualPic, loggerService,
                    mainWindowViewModel, configuration.FailOnUnhandledPort);
        biosKeyboardInt9Handler = new BiosKeyboardInt9Handler(memory,
            functionHandlerProvider, stack, state, dualPic, keyboard,
            biosDataArea, loggerService);
        mouse = new Mouse(state, dualPic, mainWindowViewModel,
                    configuration.Mouse, loggerService, configuration.FailOnUnhandledPort);
        mouseDriver = new MouseDriver(cpu, memory, mouse, mainWindowViewModel,
            vgaFunctionality, loggerService);
        keyboardInt16Handler = new KeyboardInt16Handler(
            memory, functionHandlerProvider, stack, state, loggerService,
            biosKeyboardInt9Handler.BiosKeyboardBuffer);
        joystick = new Joystick(state, ioPortDispatcher,
            configuration.FailOnUnhandledPort, loggerService);
    }

    private static void CreateSoundDevices(Configuration configuration,
        LoggerService loggerService, IPauseHandler pauseHandler, State state,
        IOPortDispatcher ioPortDispatcher, DualPic dualPic, Timer timer,
        DmaController dmaController, out SoftwareMixer softwareMixer,
        out Midi midiDevice, out PcSpeaker pcSpeaker,
        out SoundBlaster soundBlaster, out GravisUltraSound gravisUltraSound) {
        softwareMixer = new(loggerService, configuration.AudioEngine);
        midiDevice = new Midi(configuration, softwareMixer, state,
                    ioPortDispatcher, pauseHandler, configuration.Mt32RomsPath,
                    configuration.FailOnUnhandledPort, loggerService);
        pcSpeaker = new PcSpeaker(softwareMixer, state, timer.GetCounter(2),
            ioPortDispatcher, pauseHandler, loggerService, configuration.FailOnUnhandledPort);
        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(
            5, 1, 5, SbType.Sb16);
        soundBlaster = new SoundBlaster(ioPortDispatcher,
            softwareMixer, state, dmaController, dualPic,
            configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);
        gravisUltraSound = new GravisUltraSound(state, ioPortDispatcher,
            configuration.FailOnUnhandledPort, loggerService);
    }

    private static DebugWindowViewModel? CreateInternalDebuggerViewModel(
        Configuration configuration, LoggerService loggerService,
        IPauseHandler pauseHandler, State state,
        EmulatorBreakpointsManager emulatorBreakpointsManager, Memory memory,
        Stack stack, FunctionCatalogue functionCatalogue, CfgCpu cfgCpu,
        VideoState videoState, Renderer vgaRenderer,
        SoftwareMixer softwareMixer, Midi midiDevice,
        MemoryDataExporter memoryDataExporter, ITextClipboard? textClipboard,
        IHostStorageProvider? hostStorageProvider,
        IUIDispatcher? uiThreadDispatcher) {
        DebugWindowViewModel? debugWindowViewModel = null;
        if (textClipboard != null && hostStorageProvider != null
            && uiThreadDispatcher != null) {
            IMessenger messenger = WeakReferenceMessenger.Default;

            BreakpointsViewModel breakpointsViewModel = new(state, pauseHandler,
                messenger, emulatorBreakpointsManager, uiThreadDispatcher);
            CfgCpuViewModel cfgCpuViewModel = new(configuration,
                cfgCpu.ExecutionContextManager,
                pauseHandler, new PerformanceMeasurer());
            DisassemblyViewModel disassemblyVm = new(
                emulatorBreakpointsManager,
                memory, state,
                functionCatalogue.FunctionInformations,
                breakpointsViewModel, pauseHandler,
                uiThreadDispatcher, messenger, textClipboard, loggerService);
            PaletteViewModel paletteViewModel = new(
                videoState.DacRegisters.ArgbPalette,
                uiThreadDispatcher);
            SoftwareMixerViewModel softwareMixerViewModel = new(softwareMixer);
            VideoCardViewModel videoCardViewModel = new(vgaRenderer, videoState);
            CpuViewModel cpuViewModel = new(state, memory, pauseHandler,
                uiThreadDispatcher);
            MidiViewModel midiViewModel = new(midiDevice);
            StructureViewModelFactory structureViewModelFactory = new(
                configuration, state, loggerService, pauseHandler);
            MemoryViewModel memoryViewModel = new(memory, memoryDataExporter, state,
                        breakpointsViewModel, pauseHandler, messenger,
                        uiThreadDispatcher, textClipboard,
                        hostStorageProvider, structureViewModelFactory);
            StackMemoryViewModel stackMemoryViewModel = new(memory,
                memoryDataExporter, state, stack,
                breakpointsViewModel, pauseHandler, messenger,
                uiThreadDispatcher, textClipboard,
                hostStorageProvider, structureViewModelFactory,
                canCloseTab: false);

            debugWindowViewModel = new DebugWindowViewModel(messenger,
                uiThreadDispatcher, pauseHandler,
                breakpointsViewModel, disassemblyVm,
                paletteViewModel, softwareMixerViewModel,
                videoCardViewModel, cpuViewModel, midiViewModel,
                cfgCpuViewModel, memoryViewModel, stackMemoryViewModel);
        }

        return debugWindowViewModel;
    }

    private static void CreateBiosAndDosInterruptHandlers(
        Configuration configuration,
        LoggerService loggerService, State state, Memory memory,
        DualPic dualPic, CallbackHandler callbackHandler,
        InterruptVectorTable interruptVectorTable, Stack stack,
        FunctionCatalogue functionCatalogue,
        IFunctionHandlerProvider functionHandlerProvider,
        TimerInt8Handler timerInt8Handler, VgaBios vgaBios,
        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler,
        SystemBiosInt12Handler systemBiosInt12Handler,
        SystemBiosInt15Handler systemBiosInt15Handler,
        SystemClockInt1AHandler systemClockInt1AHandler,
        BiosKeyboardInt9Handler biosKeyboardInt9Handler,
        MouseDriver mouseDriver, KeyboardInt16Handler keyboardInt16Handler,
        Dos dos) {
        if (configuration.InitializeDOS is not false) {
            // memoryAsmWriter is common to InterruptInstaller and
            // AssemblyRoutineInstaller so that they both write at the
            // same address (Bios Segment F000)
            MemoryAsmWriter memoryAsmWriter = new(memory,
                new SegmentedAddress(
                    configuration.ProvidedAsmHandlersSegment, 0),
                callbackHandler);
            InterruptInstaller interruptInstaller =
                new InterruptInstaller(interruptVectorTable, memoryAsmWriter, functionCatalogue);
            AssemblyRoutineInstaller assemblyRoutineInstaller =
                new AssemblyRoutineInstaller(memoryAsmWriter, functionCatalogue);

            // Register the interrupt handlers
            interruptInstaller.InstallInterruptHandler(vgaBios);
            interruptInstaller.InstallInterruptHandler(timerInt8Handler);
            interruptInstaller.InstallInterruptHandler(biosKeyboardInt9Handler);
            interruptInstaller.InstallInterruptHandler(biosEquipmentDeterminationInt11Handler);
            interruptInstaller.InstallInterruptHandler(systemBiosInt12Handler);
            interruptInstaller.InstallInterruptHandler(systemBiosInt15Handler);
            interruptInstaller.InstallInterruptHandler(keyboardInt16Handler);
            interruptInstaller.InstallInterruptHandler(systemClockInt1AHandler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt20Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt21Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt2FHandler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt28Handler);
            if (dos.Ems is not null) {
                interruptInstaller.InstallInterruptHandler(dos.Ems);
            }

            var mouseInt33Handler = new MouseInt33Handler(memory,
                functionHandlerProvider, stack, state, loggerService, mouseDriver);
            interruptInstaller.InstallInterruptHandler(mouseInt33Handler);

            var mouseIrq12Handler = new BiosMouseInt74Handler(dualPic, memory);
            interruptInstaller.InstallInterruptHandler(mouseIrq12Handler);

            SegmentedAddress mouseDriverAddress = assemblyRoutineInstaller.
                InstallAssemblyRoutine(mouseDriver, "provided_mouse_driver");
            mouseIrq12Handler.SetMouseDriverAddress(mouseDriverAddress);
        }
    }

    public void Start() {
        if (_configuration.HeadlessMode) {
            ProgramExecutor.Run();
        } else {
            _desktop?.Start(Environment.GetCommandLineArgs());
        }
    }

    private static void SetLoggingLevel(LoggerService loggerService, Configuration configuration) {
        if (configuration.SilencedLogs) {
            loggerService.AreLogsSilenced = true;
        } else if (configuration.WarningLogs) {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Warning;
        } else if (configuration.VerboseLogs) {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
        }
    }

    private static Dictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILoggerService loggerService, Configuration configuration, Machine machine) {
        Dictionary<SegmentedAddress, FunctionInformation> res = new();
        if (configuration.OverrideSupplier == null) {
            return res;
        }

        if (loggerService.IsEnabled(LogEventLevel.Verbose)) {
            loggerService.Verbose("Override supplied: {OverrideSupplier}",
                configuration.OverrideSupplier);
        }

        foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in
            configuration.OverrideSupplier.GenerateFunctionInformations(
                loggerService, configuration, configuration.ProgramEntryPointSegment,
                machine)) {
            res.Add(element.Key, element.Value);
        }

        return res;
    }

    private static void InitializeFunctionHandlers(Configuration configuration,
        FunctionHandler cpuFunctionHandler,
        FunctionHandler cpuFunctionHandlerInExternalInterrupt) {
        bool useCodeOverride = configuration.UseCodeOverrideOption;
        cpuFunctionHandler.UseCodeOverride = useCodeOverride;
        cpuFunctionHandlerInExternalInterrupt.UseCodeOverride = useCodeOverride;
    }

    private static Dictionary<SegmentedAddress, FunctionInformation> ReadFunctionOverrides(
        Configuration configuration, Machine machine, ILoggerService loggerService) {
        if (configuration.OverrideSupplier != null) {
            return GenerateFunctionInformations(loggerService, configuration, machine);
        }
        return new Dictionary<SegmentedAddress, FunctionInformation>();
    }

    private static ClassicDesktopStyleApplicationLifetime CreateDesktopApp(AppBuilder appBuilder) {
        ClassicDesktopStyleApplicationLifetime desktop = new() {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        appBuilder.SetupWithLifetime(desktop);
        return desktop;
    }

    /// <inheritdoc cref="IDisposable" />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                ProgramExecutor.Dispose();
                Machine.Dispose();
                _mainWindowViewModel?.Dispose();
                _desktop?.Dispose();
            }

            _disposed = true;
        }
    }
}