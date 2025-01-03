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
using Spice86.Infrastructure;
using Spice86.Logging;
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
    public Machine Machine { get; }
    public ProgramExecutor ProgramExecutor { get; }
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private bool _disposed;

    public Spice86DependencyInjection(ILoggerService loggerService, Configuration configuration) {
        SetLoggingLevel(loggerService, configuration);
        IPauseHandler pauseHandler = new PauseHandler(loggerService);

        RecordedDataReader reader = new(configuration.RecordedDataDirectory, loggerService);
        ExecutionFlowRecorder executionFlowRecorder =
            reader.ReadExecutionFlowRecorderFromFileOrCreate(configuration.DumpDataOnExit is not false);
        State state = new();
        IOPortDispatcher ioPortDispatcher = new(state, loggerService, configuration.FailOnUnhandledPort);
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20Gate = new(configuration.A20Gate);
        MemoryBreakpoints memoryBreakpoints = new();
        Memory memory = new(memoryBreakpoints, ram, a20Gate,
            initializeResetVector: configuration.InitializeDOS is true);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(memoryBreakpoints, pauseHandler, state);
        var biosDataArea =
            new BiosDataArea(memory, conventionalMemorySizeKb: (ushort)Math.Clamp(ram.Size / 1024, 0, 640));
        var dualPic = new DualPic(state, ioPortDispatcher, configuration.FailOnUnhandledPort,
            configuration.InitializeDOS is false, loggerService);

        CallbackHandler callbackHandler = new(state, loggerService);
        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, state);
        FunctionHandler functionHandler = new(memory, state, executionFlowRecorder, loggerService,
            configuration.DumpDataOnExit is not false);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, state, executionFlowRecorder, loggerService,
            configuration.DumpDataOnExit is not false);
        Cpu cpu = new(interruptVectorTable, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, state,
            dualPic, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager,
            loggerService, executionFlowRecorder);

        CfgCpu cfgCpu = new(memory, state, ioPortDispatcher, callbackHandler, dualPic, emulatorBreakpointsManager,
            loggerService);

        // IO devices
        DmaController dmaController =
            new(memory, state, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);

        Joystick joystick = new Joystick(state, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);

        VideoState videoState = new();
        VgaIoPortHandler videoInt10Handler = new(state, ioPortDispatcher, loggerService, videoState,
            configuration.FailOnUnhandledPort);
        Renderer vgaRenderer = new(memory, videoState);

        SoftwareMixer softwareMixer = new(loggerService);
        Midi midiDevice = new Midi(softwareMixer, state, ioPortDispatcher, pauseHandler, configuration.Mt32RomsPath,
            configuration.FailOnUnhandledPort, loggerService);

        PcSpeaker pcSpeaker = new PcSpeaker(softwareMixer, state, ioPortDispatcher, loggerService,
            configuration.FailOnUnhandledPort);

        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.Sb16);
        SoundBlaster soundBlaster = new SoundBlaster(ioPortDispatcher, softwareMixer, state, dmaController, dualPic,
            configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);

        GravisUltraSound gravisUltraSound =
            new GravisUltraSound(state, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);

        VgaRom vgaRom = new();
        VgaFunctionality vgaFunctionality = new VgaFunctionality(memory, interruptVectorTable, ioPortDispatcher,
            biosDataArea, vgaRom,
            bootUpInTextMode: configuration.InitializeDOS is true);
        VgaBios vgaBios = new VgaBios(memory, cpu, vgaFunctionality, biosDataArea, loggerService);

        Timer timer = new Timer(configuration, state, ioPortDispatcher,
            new CounterConfiguratorFactory(configuration, state, pauseHandler, loggerService), loggerService, dualPic);
        TimerInt8Handler timerInt8Handler =
            new TimerInt8Handler(memory, cpu, dualPic, timer, biosDataArea, loggerService);

        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler =
            new BiosEquipmentDeterminationInt11Handler(memory, cpu, loggerService);
        SystemBiosInt12Handler systemBiosInt12Handler =
            new SystemBiosInt12Handler(memory, cpu, biosDataArea, loggerService);
        SystemBiosInt15Handler systemBiosInt15Handler = new SystemBiosInt15Handler(memory, cpu, a20Gate,
            configuration.InitializeDOS is not false, loggerService);
        SystemClockInt1AHandler systemClockInt1AHandler =
            new SystemClockInt1AHandler(memory, cpu, loggerService, timerInt8Handler);

        EmulatorStateSerializer emulatorStateSerializer = new(configuration, memory, state, callbackHandler,
            executionFlowRecorder, functionHandler, loggerService);

        MainWindowViewModel? mainWindowViewModel = null;
        MainWindow? mainWindow = null;
        ClassicDesktopStyleApplicationLifetime? desktop = null;
        ITextClipboard? textClipboard = null;
        IHostStorageProvider? hostStorageProvider = null;
        IUIDispatcher? uiThreadDispatcher = null;

        if (!configuration.HeadlessMode) {
            desktop = CreateDesktopApp();
            uiThreadDispatcher = new UIDispatcher(Dispatcher.UIThread);
            PerformanceViewModel performanceViewModel = new(state, pauseHandler, uiThreadDispatcher);
            mainWindow = new() {
                PerformanceViewModel = performanceViewModel
            };
            textClipboard = new TextClipboard(mainWindow.Clipboard);
            hostStorageProvider = new HostStorageProvider(mainWindow.StorageProvider, configuration,
                emulatorStateSerializer);
            mainWindowViewModel = new MainWindowViewModel(
                timer, uiThreadDispatcher, hostStorageProvider, textClipboard, configuration,
                loggerService, pauseHandler, performanceViewModel);
        }

        VgaCard vgaCard = new(mainWindowViewModel, vgaRenderer, loggerService);
        Keyboard keyboard = new Keyboard(state, ioPortDispatcher, a20Gate, dualPic, loggerService,
            mainWindowViewModel, configuration.FailOnUnhandledPort);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler =
            new BiosKeyboardInt9Handler(memory, cpu, dualPic, keyboard, biosDataArea, loggerService);
        Mouse mouse = new Mouse(state, dualPic, mainWindowViewModel, configuration.Mouse, loggerService,
            configuration.FailOnUnhandledPort);

        MouseDriver mouseDriver =
            new MouseDriver(cpu, memory, mouse, mainWindowViewModel, vgaFunctionality, loggerService);

        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(memory, cpu, loggerService,
            biosKeyboardInt9Handler.BiosKeyboardBuffer);
        Dos dos = new Dos(memory, cpu, keyboardInt16Handler, vgaFunctionality, configuration.CDrive,
            configuration.Exe, configuration.InitializeDOS is not false, configuration.Ems,
            new Dictionary<string, string> { { "BLASTER", soundBlaster.BlasterString } },
            loggerService);

        Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler,
            biosKeyboardInt9Handler,
            callbackHandler, cpu,
            cfgCpu, state, dos, gravisUltraSound, ioPortDispatcher,
            joystick, keyboard, keyboardInt16Handler, emulatorBreakpointsManager, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler, systemBiosInt15Handler, systemClockInt1AHandler,
            timer,
            timerInt8Handler,
            vgaCard, videoState, videoInt10Handler, vgaRenderer, vgaBios, vgaRom,
            dmaController, new Opl(
                state, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService,
                new AdlibGold(loggerService), OplMode.Opl3),
            softwareMixer, mouse, mouseDriver,
            vgaFunctionality, pauseHandler);

        IDictionary<SegmentedAddress, FunctionInformation> functionsInformation = reader.ReadGhidraSymbolsFromFileOrCreate();
        InitializeFunctionHandlers(configuration, machine, loggerService,
            functionsInformation, functionHandler, functionHandlerInExternalInterrupt);

        ProgramExecutor programExecutor = new(configuration, emulatorBreakpointsManager,
            emulatorStateSerializer, memory, cpu, cfgCpu, state,
            timer, dos, callbackHandler, functionHandler, executionFlowRecorder, pauseHandler,
            mainWindowViewModel,
            loggerService);

        if (configuration.InitializeDOS is not false) {
            // memoryAsmWriter is common to InterruptInstaller and AssemblyRoutineInstaller so that they both write at the same address (Bios Segment F000)
            MemoryAsmWriter memoryAsmWriter = new(memory,
                new SegmentedAddress(configuration.ProvidedAsmHandlersSegment, 0), callbackHandler);
            InterruptInstaller interruptInstaller =
                new InterruptInstaller(interruptVectorTable, memoryAsmWriter, cpu.FunctionHandler);
            AssemblyRoutineInstaller assemblyRoutineInstaller =
                new AssemblyRoutineInstaller(memoryAsmWriter, cpu.FunctionHandler);

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

            var mouseInt33Handler = new MouseInt33Handler(memory, cpu, loggerService, mouseDriver);
            interruptInstaller.InstallInterruptHandler(mouseInt33Handler);

            var mouseIrq12Handler = new BiosMouseInt74Handler(dualPic, memory);
            interruptInstaller.InstallInterruptHandler(mouseIrq12Handler);

            SegmentedAddress mouseDriverAddress = assemblyRoutineInstaller.InstallAssemblyRoutine(mouseDriver);
            mouseIrq12Handler.SetMouseDriverAddress(mouseDriverAddress);
        }

        DebugWindowViewModel? debugWindowViewModel = null;
        if (textClipboard != null && hostStorageProvider != null && uiThreadDispatcher != null) {
            IMessenger messenger = WeakReferenceMessenger.Default;
            debugWindowViewModel = new DebugWindowViewModel(state, stack, memory,
                midiDevice, videoState.DacRegisters.ArgbPalette, softwareMixer, vgaRenderer, videoState,
                cfgCpu.ExecutionContextManager, messenger, uiThreadDispatcher, textClipboard, hostStorageProvider, 
                emulatorBreakpointsManager,
                functionsInformation,
                new StructureViewModelFactory(configuration, loggerService, pauseHandler),
                pauseHandler);
        }

        if (desktop != null && mainWindow != null) {
            mainWindow.DataContext = mainWindowViewModel;
            desktop.MainWindow = mainWindow;
            desktop.Startup += (_, _) => {
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

    public void Start() {
        if (_configuration.HeadlessMode) {
            ProgramExecutor.Run();
        } else {
            _desktop?.Start(Environment.GetCommandLineArgs());
        }
    }

    private static void SetLoggingLevel(ILoggerService loggerService, Configuration configuration) {
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

        if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            loggerService.Verbose("Override supplied: {OverrideSupplier}", configuration.OverrideSupplier);
        }

        foreach (KeyValuePair<SegmentedAddress, FunctionInformation> element in configuration.OverrideSupplier
            .GenerateFunctionInformations(loggerService, configuration, configuration.ProgramEntryPointSegment, machine)) {
            res.Add(element.Key, element.Value);
        }

        return res;
    }

    private static void InitializeFunctionHandlers(Configuration configuration, Machine machine,
        ILoggerService loggerService,
        IDictionary<SegmentedAddress, FunctionInformation> functionInformations, FunctionHandler cpuFunctionHandler,
        FunctionHandler cpuFunctionHandlerInExternalInterrupt) {
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

    private static ClassicDesktopStyleApplicationLifetime CreateDesktopApp() {
        AppBuilder appBuilder = AppBuilder.Configure(() => new App())
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();
        ClassicDesktopStyleApplicationLifetime desktop = new ClassicDesktopStyleApplicationLifetime {
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