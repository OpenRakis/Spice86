namespace Spice86;

using Avalonia;
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
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.CycleBudget;
using Spice86.Logging;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;
using Spice86.Views;

using System.Diagnostics;

/// <summary>
/// Class responsible for compile-time dependency injection and runtime emulator lifecycle management
/// </summary>
public class Spice86DependencyInjection : IDisposable {
    private readonly LoggerService _loggerService;
    public Machine Machine { get; }
    public ProgramExecutor ProgramExecutor { get; }
    private readonly IGuiVideoPresentation? _gui;
    private bool _disposed;
    private bool _machineDisposedAfterRun;

    public Spice86DependencyInjection(Configuration configuration)
        : this(configuration, null) {
    }

    internal Spice86DependencyInjection(Configuration configuration, MainWindow? mainWindow) {
        LoggerService loggerService = new LoggerService();
        _loggerService = loggerService;
        SetLoggingLevel(loggerService, configuration);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Spice86 starting...");
        }

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Set logging level...");
        }

        // Create DumpContext with program hash and dump directory computation
        DumpFolderMetadata dumpContext = new(configuration.Exe, configuration.RecordedDataDirectory);
        
        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Dump context created with program hash {ProgramHash} and dump directory {DumpDirectory}",
                dumpContext.ProgramHash, dumpContext.DumpDirectory);
        }

        IPauseHandler pauseHandler = new PauseHandler(loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Pause handler created...");
        }

        RecordedDataReader reader = new(dumpContext.DumpDirectory,
            loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Recorded data reader created...");
        }

        ExecutionDump executionDump = reader.ReadExecutionDumpFromFileOrCreate();
        ExecutionFlowRecorder executionFlowRecorder = new(configuration.DumpDataOnExit is not false, executionDump);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Execution flow recorder created...");
        }

        State state = new(configuration.CpuModel);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("State created...");
        }

        // Create breakpoint holders before EmulatorBreakpointsManager to avoid circular dependency
        AddressReadWriteBreakpoints memoryReadWriteBreakpoints = new();
        AddressReadWriteBreakpoints ioReadWriteBreakpoints = new();

        IOPortDispatcher ioPortDispatcher = new(
            ioReadWriteBreakpoints, state,
            loggerService, configuration.FailOnUnhandledPort);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("IO port dispatcher created...");
        }

        IOPortHandlerRegistry ioPortHandlerRegistry = new(ioPortDispatcher, state, loggerService, configuration.FailOnUnhandledPort);

        loggerService.Information("IO system bridge created...");

        Ram ram = new(A20Gate.EndOfHighMemoryArea);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("RAM created...");
        }

        A20Gate a20Gate = new(configuration.A20Gate);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("A20 gate created...");
        }

        Memory memory = new(memoryReadWriteBreakpoints,
            ram, a20Gate,
            initializeResetVector: configuration.InitializeDOS is true);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Memory bus created...");
        }

        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state, memory, 
            memoryReadWriteBreakpoints, ioReadWriteBreakpoints);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Emulator breakpoints manager created...");
        }

        var biosDataArea =
            new BiosDataArea(memory, conventionalMemorySizeKb:
                (ushort)Math.Clamp(ram.Size / 1024, 0, 640));

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("BIOS data area created...");
        }

        ExecutionStateSlice executionStateSlice = new(state);

        var dualPic = new DualPic(ioPortHandlerRegistry, executionStateSlice, loggerService);

        if (configuration.InitializeDOS is false) {
            loggerService.Information("Masking all PIC IRQs...");
            for (uint irq = 0; irq < 16; irq++) {
                dualPic.SetIrqMask(irq, true);
            }
        }

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Dual PIC created...");
        }

        CallbackHandler callbackHandler = new(state, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Callback handler created...");
        }

        InterruptVectorTable interruptVectorTable = new(memory);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Interrupt vector table created...");
        }

        Stack stack = new(memory, state);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Stack created...");
        }

        IEnumerable<FunctionInformation> functionInformationsData =
            reader.ReadGhidraSymbolsFromFileOrCreate();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function information data read...");
        }

        FunctionCatalogue functionCatalogue = new FunctionCatalogue(
            functionInformationsData);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function catalogue created...");
        }

        FunctionHandler functionHandler = new(memory, state,
            executionFlowRecorder, functionCatalogue, configuration.UseCodeOverrideOption, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function handler created...");
        }

        FunctionHandler functionHandlerInExternalInterrupt = new(memory, state,
            executionFlowRecorder, functionCatalogue, configuration.UseCodeOverrideOption, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function handler in external interrupt created...");
        }

        Cpu cpu = new(interruptVectorTable, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, state,
            dualPic, executionStateSlice, ioPortDispatcher, callbackHandler,
            emulatorBreakpointsManager, loggerService, executionFlowRecorder);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("CPU created...");
        }

        CfgCpu cfgCpu = new(memory, state, ioPortDispatcher, callbackHandler,
            dualPic, executionStateSlice, emulatorBreakpointsManager, functionCatalogue,
            configuration.UseCodeOverrideOption, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("CfgCpu created...");
        }

        IFunctionHandlerProvider functionHandlerProvider = configuration.CfgCpu ? cfgCpu : cpu;
        IExecutionDumpFactory executionDumpFactory =
            configuration.CfgCpu ? new CfgCpuFlowDumper(cfgCpu, executionDump) : executionFlowRecorder;
        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            string cpuType = configuration.CfgCpu ? nameof(CfgCpu) : nameof(Cpu);
            loggerService.Information("Execution will be done with {CpuType}", cpuType);
        }

        // IO devices
        var timerInt8Handler = new TimerInt8Handler(dualPic, biosDataArea);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Timer int8 handler created...");
        }

        DmaSystem dmaSystem =
            new(memory, state, ioPortDispatcher,
            configuration.FailOnUnhandledPort, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("DMA controller created...");
        }

        VideoState videoState = new();
        VgaIoPortHandler vgaIoPortHandler = new(state, ioPortDispatcher,
            loggerService, videoState, configuration.FailOnUnhandledPort);
        Renderer vgaRenderer = new(memory, videoState, loggerService);
        VgaRom vgaRom = new();
        VgaFunctionality vgaFunctionality = new VgaFunctionality(memory,
            interruptVectorTable, ioPortDispatcher,
            biosDataArea, vgaRom,
            bootUpInTextMode: configuration.InitializeDOS is not false);
        VgaBios vgaBios = new VgaBios(memory, functionHandlerProvider, stack,
            state, vgaFunctionality, biosDataArea, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Video card support classes created...");
        }

        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler = new(memory,
                    functionHandlerProvider, stack, state, loggerService);
        SystemBiosInt12Handler systemBiosInt12Handler = new(memory, functionHandlerProvider, stack,
                    state, biosDataArea, loggerService);

        // memoryAsmWriter is common to InterruptInstaller and
        // AssemblyRoutineInstaller so that they both write at the
        // same address (Bios Segment F000)
        MemoryAsmWriter memoryAsmWriter = new(memory,
            new SegmentedAddress(
                configuration.ProvidedAsmHandlersSegment, 0),
            callbackHandler);

        ExtendedMemoryManager? xms = null;

        DosTables dosTables = new();

        SharedMouseData sharedMouseData = new();

        if (configuration.Xms is not false) {
            xms = new(memory, state, a20Gate, memoryAsmWriter, dosTables, loggerService);
        }
        if (configuration.Xms is not false && loggerService.IsEnabled(
            LogEventLevel.Information)) {
            loggerService.Information("DOS XMS driver created...");
        }

        SystemBiosInt15Handler systemBiosInt15Handler = new(configuration, memory,
            functionHandlerProvider, stack, state, a20Gate,
            configuration.InitializeDOS is not false, loggerService);
        var rtc = new Clock(loggerService);

        SystemClockInt1AHandler systemClockInt1AHandler = new(memory,
            functionHandlerProvider, stack,
            state, loggerService, timerInt8Handler, rtc);
        SystemBiosInt13Handler systemBiosInt13Handler = new(memory,
            functionHandlerProvider, stack, state, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("BIOS interrupt handlers created...");
        }

        SoftwareMixer softwareMixer = new(loggerService, configuration.AudioEngine);
        var midiDevice = new Midi(configuration, softwareMixer, state,
            ioPortDispatcher, pauseHandler, configuration.Mt32RomsPath,
            configuration.FailOnUnhandledPort, loggerService);
        PcSpeaker pcSpeaker = new(softwareMixer, state, ioPortDispatcher,
            pauseHandler, loggerService, dualPic, configuration.FailOnUnhandledPort);
        PitTimer pitTimer = new(ioPortHandlerRegistry, dualPic, pcSpeaker, loggerService);
        pcSpeaker.AttachPitControl(pitTimer);
        loggerService.Information("PIT created...");

        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.SbPro2);
        loggerService.Information("SoundBlaster configured with {SBConfig}", soundBlasterHardwareConfig);
        var soundBlaster = new SoundBlaster(ioPortDispatcher,
            softwareMixer, state, dmaSystem, dualPic,
            configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);
        var gravisUltraSound = new GravisUltraSound(state, ioPortDispatcher,
            configuration.FailOnUnhandledPort, loggerService);

        loggerService.Information("Sound devices created...");

        MemoryDataExporter memoryDataExporter = new(memory, callbackHandler,
            configuration, dumpContext.DumpDirectory, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Memory data exporter created...");
        }
       
        EmulatorStateSerializer emulatorStateSerializer = new(dumpContext,
            memoryDataExporter, state, executionDumpFactory, functionCatalogue,
            emulatorBreakpointsManager, loggerService);

        SerializableUserBreakpointCollection deserializedUserBreakpoints =
              emulatorStateSerializer.LoadBreakpoints(dumpContext.DumpDirectory);
      
        IInstructionExecutor cpuForEmulationLoop = configuration.CfgCpu ? cfgCpu : cpu;

        ICyclesLimiter cyclesLimiter = CycleLimiterFactory.Create(configuration);
        ICyclesBudgeter cyclesBudgeter = configuration.CyclesBudgeter ?? CreateDefaultCyclesBudgeter(cyclesLimiter);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Emulator state serializer created...");
        }

        MainWindowViewModel? mainWindowViewModel = null;
        UIDispatcher? uiDispatcher = null;
        HostStorageProvider? hostStorageProvider = null;
        TextClipboard? textClipboard = null;
        PerformanceMeasurer cpuPerformanceMeasurer = new(new PerformanceMeasureOptions {
            CheckInterval = 512,
            MinValueDelta = 3000,
            MaxIntervalMilliseconds = 10
        });
        EmulationLoop emulationLoop;
        InputEventHub inputEventHub;

        if (mainWindow != null) {
            uiDispatcher = new UIDispatcher(Dispatcher.UIThread);
            hostStorageProvider = new HostStorageProvider(
                mainWindow.StorageProvider, configuration, emulatorStateSerializer, dumpContext);
            textClipboard = new TextClipboard(mainWindow.Clipboard);

            PerformanceViewModel performanceViewModel = new(
                state, pauseHandler, uiDispatcher, cpuPerformanceMeasurer);

            mainWindow.PerformanceViewModel = performanceViewModel;

            IExceptionHandler exceptionHandler = configuration.HeadlessMode switch {
                null => new MainWindowExceptionHandler(pauseHandler),
                _ => new HeadlessModeExceptionHandler(uiDispatcher)
            };

            mainWindowViewModel = new MainWindowViewModel(sharedMouseData,
                pitTimer, uiDispatcher, hostStorageProvider, textClipboard, configuration,
                loggerService, pauseHandler, performanceViewModel, exceptionHandler, cyclesLimiter);

            inputEventHub = new(mainWindowViewModel, mainWindowViewModel);

            emulationLoop = new(
                functionHandler, cpuForEmulationLoop,
                state, executionStateSlice, dualPic, emulatorBreakpointsManager,
                cpuPerformanceMeasurer, pauseHandler, cyclesLimiter, inputEventHub, cyclesBudgeter, loggerService);

            _gui = mainWindowViewModel;
        } else {
            HeadlessGui headlessGui = new HeadlessGui();
            _gui = headlessGui;
            inputEventHub = new InputEventHub(headlessGui, headlessGui);
            emulationLoop = new(
                functionHandler, cpuForEmulationLoop,
                state, executionStateSlice, dualPic, emulatorBreakpointsManager,
                cpuPerformanceMeasurer, pauseHandler, cyclesLimiter,
                inputEventHub, cyclesBudgeter, loggerService);
        }

        VgaCard vgaCard = new(_gui, vgaRenderer, loggerService);
        vgaCard.SubscribeToEvents();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("VGA card created...");
        }

        Intel8042Controller intel8042Controller = new(
            state, ioPortDispatcher, a20Gate, dualPic,
            configuration.FailOnUnhandledPort, pauseHandler, loggerService, inputEventHub);

        BiosKeyboardBuffer biosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new(memory, biosDataArea,
            stack, state, functionHandlerProvider, dualPic, systemBiosInt15Handler,
            intel8042Controller, biosKeyboardBuffer, loggerService);
        Mouse mouse = new(state, sharedMouseData, dualPic,
                    configuration.Mouse, loggerService, configuration.FailOnUnhandledPort,
                     _gui as IGuiMouseEvents);
        MouseDriver mouseDriver = new(state, sharedMouseData, memory, mouse,
            vgaFunctionality, loggerService,
             _gui as IGuiMouseEvents);

        KeyboardInt16Handler keyboardInt16Handler = new(
            memory, biosDataArea, functionHandlerProvider, stack, state, loggerService,
            biosKeyboardInt9Handler.BiosKeyboardBuffer);

        Joystick joystick = new(state, ioPortDispatcher,
    configuration.FailOnUnhandledPort, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Input devices created...");
        }

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Emulation loop created...");
        }
        InterruptInstaller interruptInstaller =
            new InterruptInstaller(interruptVectorTable, memoryAsmWriter, functionCatalogue);
        AssemblyRoutineInstaller assemblyRoutineInstaller =
            new AssemblyRoutineInstaller(memoryAsmWriter, functionCatalogue);

        var dummyInt1CHandler = new DummyInt1CHandler();

        BiosMouseInt74Handler? mouseIrq12Handler = null;
        if (configuration.InitializeDOS is not false) {
            // Register the BIOS interrupt handlers
            interruptInstaller.InstallInterruptHandler(vgaBios);
            interruptInstaller.InstallInterruptHandler(dummyInt1CHandler);
            interruptInstaller.InstallInterruptHandler(timerInt8Handler);
            interruptInstaller.InstallInterruptHandler(biosKeyboardInt9Handler);
            interruptInstaller.InstallInterruptHandler(biosEquipmentDeterminationInt11Handler);
            interruptInstaller.InstallInterruptHandler(systemBiosInt12Handler);
            interruptInstaller.InstallInterruptHandler(systemBiosInt15Handler);
            interruptInstaller.InstallInterruptHandler(keyboardInt16Handler);
            interruptInstaller.InstallInterruptHandler(systemClockInt1AHandler);
            interruptInstaller.InstallInterruptHandler(systemBiosInt13Handler);
            mouseIrq12Handler = new BiosMouseInt74Handler(dualPic, memory);
            interruptInstaller.InstallInterruptHandler(mouseIrq12Handler);
            InstallDefaultInterruptHandlers(interruptInstaller, dualPic, biosDataArea, loggerService);
        }

        var dosClock = new Clock(loggerService);
        Dos dos = new Dos(configuration, memory, functionHandlerProvider, stack,
            state, biosKeyboardBuffer,
            keyboardInt16Handler, biosDataArea, vgaFunctionality,
            new Dictionary<string, string> {
                { "BLASTER", soundBlaster.BlasterString } }, dosClock, loggerService,
            xms);

        if (configuration.InitializeDOS is not false) {
            // Register the DOS interrupt handlers
            interruptInstaller.InstallInterruptHandler(dos.DosInt20Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt21Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt2FHandler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt25Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt26Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt28Handler);
            if (dos.Ems is not null) {
                interruptInstaller.InstallInterruptHandler(dos.Ems);
            }

            var mouseInt33Handler = new MouseInt33Handler(memory,
                functionHandlerProvider, stack, state, loggerService, mouseDriver);
            interruptInstaller.InstallInterruptHandler(mouseInt33Handler);

            SegmentedAddress mouseDriverAddress = assemblyRoutineInstaller.
                InstallAssemblyRoutine(mouseDriver, "provided_mouse_driver");
            mouseIrq12Handler?.SetMouseDriverAddress(mouseDriverAddress);
        }

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Disk operating system created...");
        }

        Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler,
            biosKeyboardInt9Handler,
            callbackHandler, cpu,
            cfgCpu, state, dos, gravisUltraSound, ioPortDispatcher,
            joystick, intel8042Controller, keyboardInt16Handler,
            emulatorBreakpointsManager, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler,
            systemBiosInt15Handler, systemClockInt1AHandler,
            pitTimer,
            timerInt8Handler,
            vgaCard, videoState, vgaIoPortHandler,
            vgaRenderer, vgaBios, vgaRom,
            dmaSystem, soundBlaster.Opl3Fm, softwareMixer, mouse, mouseDriver,
            vgaFunctionality, pauseHandler);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Machine created...");
        }

        DictionaryUtils.AddAll(functionCatalogue.FunctionInformations,
            ReadFunctionOverrides(configuration, machine, loggerService));

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function overrides added...");
        }

        ProgramExecutor programExecutor = new(configuration, emulationLoop,
            emulatorBreakpointsManager, emulatorStateSerializer, memory,
            functionHandlerProvider, memoryDataExporter, state, dos,
            functionCatalogue, executionDumpFactory, pauseHandler,
            mainWindowViewModel, dumpContext, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Program executor created...");
        }

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("BIOS and DOS interrupt handlers created...");
        }

        Machine = machine;
        ProgramExecutor = programExecutor;
        ProgramExecutor.EmulationStopped += OnProgramExecutorEmulationStopped;

        if (mainWindow != null && uiDispatcher != null &&
            hostStorageProvider != null && textClipboard != null) {
            IMessenger messenger = WeakReferenceMessenger.Default;

            BreakpointsViewModel breakpointsViewModel = new(
                state, pauseHandler, messenger, emulatorBreakpointsManager, uiDispatcher, textClipboard, memory);
            
            breakpointsViewModel.RestoreBreakpoints(deserializedUserBreakpoints);

            DisassemblyViewModel disassemblyViewModel = new(
                emulatorBreakpointsManager, memory, state, functionCatalogue.FunctionInformations,
                breakpointsViewModel, pauseHandler, uiDispatcher, messenger, textClipboard, loggerService,
                canCloseTab: false);

            PaletteViewModel paletteViewModel = new(videoState.DacRegisters.ArgbPalette,
                uiDispatcher);

            SoftwareMixerViewModel softwareMixerViewModel = new(softwareMixer);

            VideoCardViewModel videoCardViewModel = new(vgaRenderer, videoState, hostStorageProvider);

            CpuViewModel cpuViewModel = new(state, memory, pauseHandler, uiDispatcher);

            MidiViewModel midiViewModel = new(midiDevice);

            CfgCpuViewModel cfgCpuViewModel = new(configuration, uiDispatcher,
                cfgCpu.ExecutionContextManager, pauseHandler);

            StructureViewModelFactory structureViewModelFactory = new(configuration,
                state, loggerService, pauseHandler);

            MemoryViewModel memoryViewModel = new(memory, memoryDataExporter, state,
                breakpointsViewModel, pauseHandler, messenger, uiDispatcher,
                textClipboard, hostStorageProvider, structureViewModelFactory,
                canCloseTab: false);

            StackMemoryViewModel stackMemoryViewModel = new(memory, memoryDataExporter, state, stack,
                breakpointsViewModel, pauseHandler, messenger, uiDispatcher,
                textClipboard, hostStorageProvider, structureViewModelFactory,
                canCloseTab: false);

            DataSegmentMemoryViewModel dataSegmentViewModel = new(memory, memoryDataExporter, state,
                breakpointsViewModel, pauseHandler, messenger, uiDispatcher,
                textClipboard, hostStorageProvider, structureViewModelFactory,
                canCloseTab: false);

            DebugWindowViewModel debugWindowViewModel = new(
                WeakReferenceMessenger.Default, uiDispatcher, pauseHandler,
                breakpointsViewModel, disassemblyViewModel,
                paletteViewModel, softwareMixerViewModel, videoCardViewModel,
                cpuViewModel, midiViewModel, cfgCpuViewModel,
                [memoryViewModel, stackMemoryViewModel, dataSegmentViewModel]);

            Application.Current!.Resources[nameof(DebugWindowViewModel)] =
                debugWindowViewModel;
            mainWindow.DataContext = mainWindowViewModel;
        }
    }

    private static ICyclesBudgeter CreateDefaultCyclesBudgeter(ICyclesLimiter cyclesLimiter) {
        long sliceDurationTicks = Math.Max(1, Stopwatch.Frequency / 1000);
        double sliceDurationMilliseconds = sliceDurationTicks * 1000.0 / Stopwatch.Frequency;
        ICyclesBudgeter cyclesBudgeter = new AdaptiveCyclesBudgeter(cyclesLimiter, sliceDurationMilliseconds);
        return cyclesBudgeter;
    }

    private readonly byte[] _defaultIrqs = [3, 4, 5, 7, 10, 11];

    private void InstallDefaultInterruptHandlers(InterruptInstaller interruptInstaller, DualPic dualPic,
        BiosDataArea biosDataArea, LoggerService loggerService)
    {
        _loggerService.Information("Installing default interrupt handlers for IRQs {IRQs}...",
            string.Join(", ", _defaultIrqs));
        foreach (byte irq in _defaultIrqs) {
            interruptInstaller.InstallInterruptHandler(new DefaultIrqHandler(dualPic, irq, biosDataArea,
                loggerService));
        }
    }

    public void HeadlessModeStart() {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Finally starting headless mode...");
        }
        ProgramExecutor.Run();
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
    private static Dictionary<SegmentedAddress, FunctionInformation> ReadFunctionOverrides(
        Configuration configuration, Machine machine, ILoggerService loggerService) {
        if (configuration.OverrideSupplier != null) {
            return GenerateFunctionInformations(loggerService, configuration, machine);
        }
        return new Dictionary<SegmentedAddress, FunctionInformation>();
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
                ProgramExecutor.EmulationStopped -= OnProgramExecutorEmulationStopped;
                ProgramExecutor.Dispose();

                // Dispose HeadlessGui BEFORE Machine to stop the rendering timer
                // before the Renderer is disposed. This prevents a race condition
                // where the timer callback tries to render with a disposed Renderer.
                if (_gui is HeadlessGui headlessGui) {
                    headlessGui.Dispose();
                }

                DisposeMachineAfterRun();
            }
            _disposed = true;
        }
    }

    private void OnProgramExecutorEmulationStopped(object? sender, EventArgs e) {
        DisposeMachineAfterRun();
    }

    private void DisposeMachineAfterRun() {
        if (_machineDisposedAfterRun) {
            return;
        }

        _machineDisposedAfterRun = true;
        Machine.Dispose();
    }
}