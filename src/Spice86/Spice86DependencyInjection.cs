namespace Spice86;

using Avalonia;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.Devices.Cmos;
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
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Logging;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;
using Spice86.Views;

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

        // Create folder for emulator state serialization / deserialization
        EmulatorStateSerializationFolderFactory emulatorStateSerializationFolderFactory = new(_loggerService);
        EmulatorStateSerializationFolder emulatorStateSerializationFolder =
            emulatorStateSerializationFolderFactory.ComputeFolder(configuration.Exe, configuration.RecordedDataDirectory);

        IPauseHandler pauseHandler = new PauseHandler(loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Pause handler created...");
        }


        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Wall clock created...");
        }

        EmulationStateDataReader emulationStateDataReader = new(emulatorStateSerializationFolder, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Recorded data reader created...");
        }

        ExecutionAddresses executionAddresses = emulationStateDataReader.ReadExecutionDataFromFileOrCreate();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Execution data read...");
        }

        State state = new(configuration.CpuModel);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("State created...");
        }

        // Create breakpoint holders before EmulatorBreakpointsManager to avoid circular dependency
        AddressReadWriteBreakpoints memoryReadWriteBreakpoints = new();
        AddressReadWriteBreakpoints ioReadWriteBreakpoints = new();

        ICyclesLimiter cyclesLimiter = CycleLimiterFactory.Create(state, configuration);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Cycles limiter created...");
        }

        IOPortDispatcher ioPortDispatcher = new(
            ioReadWriteBreakpoints, state,
            loggerService, configuration.FailOnUnhandledPort);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("IO port dispatcher created...");
        }

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

        IEmulatedClock emulatedClock = configuration.InstructionsPerSecond != null
            ? new CyclesClock(state, configuration.InstructionsPerSecond.Value)
            : new EmulatedClock();

        // Register clock and limiter to pause/resume events
        pauseHandler.Pausing += () => emulatedClock.OnPause();
        pauseHandler.Resumed += () => emulatedClock.OnResume();
        
        EmulationLoopScheduler emulationLoopScheduler = new(emulatedClock, loggerService);

        var dualPic = new DualPic(ioPortDispatcher, state, loggerService, configuration.FailOnUnhandledPort);

        if (configuration.InitializeDOS is false) {
            loggerService.Information("Masking all PIC IRQs...");
            for (uint irq = 0; irq < 16; irq++) {
                dualPic.SetIrqMask(irq, true);
            }
        }

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Dual PIC created...");
        }

        RealTimeClock realTimeClock = new(state, ioPortDispatcher, dualPic,
            emulationLoopScheduler, emulatedClock, configuration.FailOnUnhandledPort, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("RTC/CMOS created...");
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
            emulationStateDataReader.ReadGhidraSymbolsFromFileOrCreate();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function information data read...");
        }

        FunctionCatalogue functionCatalogue = new FunctionCatalogue(
            functionInformationsData);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function catalogue created...");
        }

        FunctionHandler functionHandler = new(memory, state,
            functionCatalogue, configuration.UseCodeOverrideOption, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function handler created...");
        }

        // Create CPU heavy logger if enabled
        AsmRenderingConfig asmRenderingConfig = AsmRenderingConfig.Create(configuration.AsmRenderingStyle);
        NodeToString nodeToString = new NodeToString(asmRenderingConfig);
        CpuHeavyLogger? cpuHeavyLogger = null;
        if (configuration.CpuHeavyLog) {
            cpuHeavyLogger = new CpuHeavyLogger(emulatorStateSerializationFolder, configuration.CpuHeavyLogDumpFile, nodeToString, state, asmRenderingConfig);
            if (loggerService.IsEnabled(LogEventLevel.Information)) {
                loggerService.Information("CPU heavy logger created. Logging to: {LogFile}", 
                    configuration.CpuHeavyLogDumpFile ?? Path.Join(emulatorStateSerializationFolder.Folder, "cpu_heavy.log"));
            }
        }

        CfgCpu cfgCpu = new(memory, state, ioPortDispatcher, callbackHandler,
            dualPic, emulatorBreakpointsManager, functionCatalogue,
            configuration.UseCodeOverrideOption, configuration.FailOnInvalidOpcode, loggerService, cpuHeavyLogger);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("CfgCpu created...");
        }

        // IO devices
        var timerInt8Handler = new TimerInt8Handler(dualPic, biosDataArea);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Timer int8 handler created...");
        }

        DmaBus dmaSystem =
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
        VgaBios vgaBios = new VgaBios(memory, cfgCpu, stack,
            state, vgaFunctionality, biosDataArea, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Video card support classes created...");
        }

        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler = new(memory,
            cfgCpu, stack, state, loggerService);
        SystemBiosInt12Handler systemBiosInt12Handler = new(memory, cfgCpu, stack,
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
            cfgCpu, stack, state, a20Gate, biosDataArea, emulationLoopScheduler,
            ioPortDispatcher, loggerService, configuration.InitializeDOS is not false);

        SystemClockInt1AHandler systemClockInt1AHandler = new(memory, biosDataArea,
            realTimeClock, cfgCpu, stack, state, loggerService);
        SystemBiosInt13Handler systemBiosInt13Handler = new(memory,
            cfgCpu, stack, state, loggerService);
        RtcInt70Handler rtcInt70Handler = new(memory, cfgCpu, stack, state,
            dualPic, biosDataArea, ioPortDispatcher, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("BIOS interrupt handlers created...");
        }

        Mixer mixer = new(loggerService, configuration.AudioEngine, pauseHandler);
        var midiDevice = new Midi(configuration, mixer, state,
            ioPortDispatcher, pauseHandler, configuration.Mt32RomsPath,
            configuration.FailOnUnhandledPort, loggerService);
        PcSpeaker pcSpeaker = new(mixer, state, ioPortDispatcher,
            pauseHandler, loggerService, emulationLoopScheduler, emulatedClock, configuration.FailOnUnhandledPort);

        PitTimer pitTimer = new(ioPortDispatcher, state, dualPic, pcSpeaker, emulationLoopScheduler, emulatedClock,
            loggerService, configuration.FailOnUnhandledPort);

        pcSpeaker.AttachPitControl(pitTimer);
        loggerService.Information("PIT created...");

        // Create OPL FM device; it creates and registers its own mixer channel internally
        SoundBlasterHardwareConfig soundBlasterHardwareConfig = new(
            configuration.SbIrq,
            configuration.SbDma,
            configuration.SbHdma,
            configuration.SbType,
            configuration.SbBase);
        loggerService.Information("SoundBlaster configured with {SBConfig}", soundBlasterHardwareConfig);

        Opl OPL = new(mixer, state, ioPortDispatcher,
            configuration.FailOnUnhandledPort, loggerService,
            emulationLoopScheduler, emulatedClock, cyclesLimiter, dualPic,
            mode: configuration.OplMode, sbBase: configuration.SbBase, enableOplIrq: false);

        SoundBlaster soundBlaster = new(ioPortDispatcher,
            state, dmaSystem, dualPic, mixer, OPL, loggerService,
            emulationLoopScheduler, emulatedClock,
            soundBlasterHardwareConfig);
        GravisUltraSound gravisUltraSound = new(state, ioPortDispatcher,
            configuration.FailOnUnhandledPort, loggerService);

        loggerService.Information("Sound devices created...");

        MemoryDataExporter memoryDataExporter = new(memory, callbackHandler, configuration, loggerService);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Memory data exporter created...");
        }

        ListingExporter listingExporter = new(cfgCpu, loggerService, nodeToString);

        ExecutionAddressesExtractor executionAddressesExtractor = new(cfgCpu, executionAddresses);
        EmulationStateDataWriter emulationStateDataWriter = new(state, executionAddressesExtractor, memoryDataExporter,
            listingExporter, functionCatalogue, emulatorStateSerializationFolder, emulatorBreakpointsManager,
            loggerService);
        EmulatorStateSerializer emulatorStateSerializer = new(emulatorStateSerializationFolder,
            emulationStateDataReader, emulationStateDataWriter);

        SerializableUserBreakpointCollection deserializedUserBreakpoints =
            emulationStateDataReader.ReadBreakpointsFromFileOrCreate();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Emulator state serializer created...");
        }

        MainWindowViewModel? mainWindowViewModel = null;
        UIDispatcher? uiDispatcher = null;
        HostStorageProvider? hostStorageProvider = null;
        TextClipboard? textClipboard = null;
        InputEventHub inputEventHub;

        if (mainWindow != null) {
            uiDispatcher = new UIDispatcher(Dispatcher.UIThread);
            hostStorageProvider = new HostStorageProvider(
                mainWindow.StorageProvider, emulatorStateSerializer);
            textClipboard = new TextClipboard(mainWindow.Clipboard);

            PerformanceTracker performanceTracker = new PerformanceTracker(new SystemTimeProvider());

            PerformanceViewModel performanceViewModel = new(
                state, pauseHandler, uiDispatcher, performanceTracker);

            mainWindow.PerformanceViewModel = performanceViewModel;

            IExceptionHandler exceptionHandler = configuration.HeadlessMode switch {
                null => new MainWindowExceptionHandler(pauseHandler),
                _ => new HeadlessModeExceptionHandler(uiDispatcher)
            };

            mainWindowViewModel = new MainWindowViewModel(sharedMouseData,
                pitTimer, uiDispatcher, hostStorageProvider, textClipboard, configuration,
                loggerService, pauseHandler, performanceViewModel, exceptionHandler, cyclesLimiter,
                mixer, soundBlaster, OPL);

            // Subscribe to video mode changes for dynamic aspect ratio correction
            vgaFunctionality.VideoModeChanged += mainWindowViewModel.OnVideoModeChanged;

            inputEventHub = new(mainWindowViewModel, mainWindowViewModel);

            _gui = mainWindowViewModel;
        } else {
            HeadlessGui headlessGui = new HeadlessGui();
            _gui = headlessGui;
            inputEventHub = new InputEventHub(headlessGui, headlessGui);
        }

        EmulationLoop emulationLoop = new(
            functionHandler, cfgCpu,
            state, emulationLoopScheduler, emulatorBreakpointsManager,
            pauseHandler, inputEventHub, cyclesLimiter, loggerService);

        VgaCard vgaCard = new(_gui, vgaRenderer, loggerService);
        vgaCard.SubscribeToEvents();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("VGA card created...");
        }

        Intel8042Controller intel8042Controller = new(
            state, ioPortDispatcher, a20Gate, dualPic, emulationLoopScheduler,
            configuration.FailOnUnhandledPort, loggerService, inputEventHub);

        BiosKeyboardBuffer biosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new(memory, biosDataArea,
            stack, state, cfgCpu, dualPic, systemBiosInt15Handler,
            intel8042Controller, biosKeyboardBuffer, loggerService);
        Mouse mouse = new(state, sharedMouseData, dualPic,
            configuration.Mouse, loggerService, configuration.FailOnUnhandledPort,
            _gui as IGuiMouseEvents);
        MouseDriver mouseDriver = new(state, sharedMouseData, memory, mouse,
            vgaFunctionality, loggerService,
            _gui as IGuiMouseEvents);

        KeyboardInt16Handler keyboardInt16Handler = new(
            memory, ioPortDispatcher, biosDataArea, cfgCpu, stack, state, loggerService,
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
            interruptInstaller.InstallInterruptHandler(rtcInt70Handler);
            mouseIrq12Handler = new BiosMouseInt74Handler(dualPic, memory);
            interruptInstaller.InstallInterruptHandler(mouseIrq12Handler);
            InstallDefaultInterruptHandlers(interruptInstaller, dualPic, biosDataArea, loggerService);
        }

        Dos dos = new Dos(configuration, memory, cfgCpu, stack,
            state, biosKeyboardBuffer,
            keyboardInt16Handler, biosDataArea, vgaFunctionality,
            new Dictionary<string, string> {
                { "BLASTER", soundBlaster.BlasterString } }, ioPortDispatcher, loggerService,
            xms);

        if (configuration.InitializeDOS is not false) {
            // Register the DOS interrupt handlers
            interruptInstaller.InstallInterruptHandler(dos.DosInt22Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt20Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt21Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt23Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt24Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt2FHandler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt25Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt26Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt28Handler);
            interruptInstaller.InstallInterruptHandler(dos.DosInt2aHandler);
            if (dos.Ems is not null) {
                interruptInstaller.InstallInterruptHandler(dos.Ems);
            }

            var mouseInt33Handler = new MouseInt33Handler(memory,
                cfgCpu, stack, state, loggerService, mouseDriver);
            interruptInstaller.InstallInterruptHandler(mouseInt33Handler);

            SegmentedAddress mouseDriverAddress =
                assemblyRoutineInstaller.InstallAssemblyRoutine(mouseDriver, "provided_mouse_driver");
            mouseIrq12Handler?.SetMouseDriverAddress(mouseDriverAddress);
        }

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Disk operating system created...");
        }

        Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler,
            biosKeyboardInt9Handler,
            callbackHandler,
            cfgCpu, state, stack, dos, gravisUltraSound, ioPortDispatcher,
            joystick, intel8042Controller, interruptVectorTable, keyboardInt16Handler,
            emulatorBreakpointsManager, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler,
            systemBiosInt15Handler, systemClockInt1AHandler, realTimeClock,
            pitTimer,
            timerInt8Handler,
            vgaCard, videoState, vgaIoPortHandler,
            vgaRenderer, vgaBios, vgaRom,
            dmaSystem, OPL, mixer, mouse, mouseDriver,
            vgaFunctionality, pauseHandler);

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Machine created...");
        }

        DictionaryUtils.AddAll(functionCatalogue.FunctionInformations,
            ReadFunctionOverrides(configuration, machine, loggerService));

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Function overrides added...");
        }

        ProgramExecutor programExecutor = new(
            configuration,
            emulationLoop,
            emulatorBreakpointsManager,
            emulatorStateSerializer,
            memory,
            cfgCpu,
            state,
            dos.DosInt21Handler,
            pauseHandler,
            mainWindowViewModel,
            loggerService);

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

            VideoCardViewModel videoCardViewModel = new(vgaRenderer, videoState, hostStorageProvider);

            CpuViewModel cpuViewModel = new(state, memory, pauseHandler, uiDispatcher);

            MidiViewModel midiViewModel = new(midiDevice);

            CfgCpuViewModel cfgCpuViewModel = new(uiDispatcher,
                cfgCpu.ExecutionContextManager, pauseHandler, nodeToString);

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
                paletteViewModel, videoCardViewModel,
                cpuViewModel, midiViewModel, cfgCpuViewModel,
                [memoryViewModel, stackMemoryViewModel, dataSegmentViewModel]);

            Application.Current!.Resources[nameof(DebugWindowViewModel)] =
                debugWindowViewModel;
            mainWindow.DataContext = mainWindowViewModel;
        }
    }

    private readonly byte[] _defaultIrqs = [3, 4, 5, 7, 10, 11];

    private void InstallDefaultInterruptHandlers(InterruptInstaller interruptInstaller, DualPic dualPic,
        BiosDataArea biosDataArea, LoggerService loggerService) {
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