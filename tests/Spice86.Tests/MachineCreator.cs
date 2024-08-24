namespace Spice86.Tests;

using System;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
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
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Timer = Spice86.Core.Emulator.Devices.Timer.Timer;

public class MachineCreator {
    public (Machine Machine, ProgramExecutor ProgramExecutor) CreateProgramExecutorFromBinName(string binName,  bool enablePit, bool recordData = false) {
        return CreateProgramExecutorForBin($"Resources/cpuTests/{binName}.bin", enablePit, recordData);
    }

    public (Machine Machine, ProgramExecutor ProgramExecutor) CreateProgramExecutorForBin(string binPath, bool enablePit, bool recordData = false) {
        Configuration configuration = new Configuration {
            // making sure int8 is not going to be triggered during the tests
            Exe = binPath,
            // Don't expect any hash for the exe
            ExpectedChecksumValue = Array.Empty<byte>(),
            InitializeDOS = false,
            DumpDataOnExit = recordData,
            TimeMultiplier = enablePit ? 1 : 0,
            // Use instructions per second based timer for predictability if timer is enabled
            InstructionsPerSecond = enablePit ? 100000 : null
        };

        ILoggerService loggerService = Substitute.For<ILoggerService>();
        PauseHandler pauseHandler = new(loggerService);
        
        RecordedDataReader reader = new(configuration.RecordedDataDirectory, loggerService);
        ExecutionFlowRecorder executionFlowRecorder = reader.ReadExecutionFlowRecorderFromFileOrCreate(configuration.DumpDataOnExit is not false);
        State cpuState = new();
        IOPortDispatcher ioPortDispatcher = new(cpuState, loggerService, configuration.FailOnUnhandledPort);
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20gate = new(configuration.A20Gate);
        MemoryBreakpoints memoryBreakpoints = new();
        IMemory memory = new Memory(memoryBreakpoints, ram, a20gate, initializeResetVector: configuration.InitializeDOS is true);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(memoryBreakpoints, pauseHandler, cpuState);

        var biosDataArea = new BiosDataArea(memory, conventionalMemorySizeKb: (ushort)Math.Clamp(ram.Size / 1024, 0, 640));
        var dualPic = new DualPic(cpuState, ioPortDispatcher, configuration.FailOnUnhandledPort, configuration.InitializeDOS is false, loggerService);

        CallbackHandler callbackHandler = new(cpuState, loggerService);
        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, cpuState);
        FunctionHandler functionHandler = new(memory, cpuState, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, cpuState, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        Cpu cpu  = new(interruptVectorTable, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, cpuState,
            dualPic, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager,
            loggerService, executionFlowRecorder);
        
        // IO devices
        using DmaController dmaController = new(memory, cpuState, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);

        VideoState videoState = new();
        VgaIoPortHandler videoInt10Handler = new(cpuState, ioPortDispatcher, loggerService, videoState, configuration.FailOnUnhandledPort);

        IGui? gui = null;
        Renderer renderer = new(memory, videoState);
        VgaCard vgaCard = new(gui, renderer, loggerService);
        Timer timer = new Timer(configuration, cpuState, ioPortDispatcher, loggerService, dualPic);
        Keyboard keyboard = new Keyboard(cpuState, ioPortDispatcher, a20gate, dualPic, loggerService, gui, configuration.FailOnUnhandledPort);
        Mouse mouse = new Mouse(cpuState, dualPic, gui, configuration.Mouse, loggerService, configuration.FailOnUnhandledPort);
        Joystick joystick = new Joystick(cpuState, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);
        
        SoftwareMixer softwareMixer = new(loggerService);
        
        PcSpeaker pcSpeaker = new PcSpeaker(
            softwareMixer, cpuState, ioPortDispatcher,
            loggerService, configuration.FailOnUnhandledPort);
        
        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.Sb16);
        SoundBlaster soundBlaster = new SoundBlaster(
            ioPortDispatcher, softwareMixer, cpuState, dmaController, dualPic, configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);
        
        GravisUltraSound gravisUltraSound = new GravisUltraSound(cpuState, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);
        
        Midi midiDevice = new Midi(softwareMixer, cpuState, ioPortDispatcher, pauseHandler, configuration.Mt32RomsPath, configuration.FailOnUnhandledPort, loggerService);

        // Services
        // memoryAsmWriter is common to InterruptInstaller and AssemblyRoutineInstaller so that they both write at the same address (Bios Segment F000)
        MemoryAsmWriter memoryAsmWriter = new(memory, new SegmentedAddress(configuration.ProvidedAsmHandlersSegment, 0), callbackHandler);
        InterruptInstaller interruptInstaller = new InterruptInstaller(interruptVectorTable, memoryAsmWriter, cpu.FunctionHandler);
        AssemblyRoutineInstaller assemblyRoutineInstaller = new AssemblyRoutineInstaller(memoryAsmWriter, cpu.FunctionHandler);

        VgaFunctionality vgaFunctionality = new VgaFunctionality(interruptVectorTable, memory, ioPortDispatcher, biosDataArea, configuration.InitializeDOS is true);
        VgaBios vgaBios = new VgaBios(memory, cpu, vgaFunctionality, biosDataArea, loggerService);

        TimerInt8Handler timerInt8Handler = new TimerInt8Handler(memory, cpu, dualPic, timer, biosDataArea, loggerService);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new BiosKeyboardInt9Handler(memory, cpu, dualPic, keyboard, biosDataArea, loggerService);

        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(memory, cpu, loggerService);
        SystemBiosInt12Handler systemBiosInt12Handler = new SystemBiosInt12Handler(memory, cpu, biosDataArea, loggerService);
        SystemBiosInt15Handler systemBiosInt15Handler = new SystemBiosInt15Handler(memory, cpu, a20gate, loggerService);
        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(memory, cpu, loggerService, biosKeyboardInt9Handler.BiosKeyboardBuffer);

        SystemClockInt1AHandler systemClockInt1AHandler = new SystemClockInt1AHandler(memory, cpu, loggerService, timerInt8Handler);

        MouseDriver mouseDriver = new MouseDriver(cpu, memory, mouse, gui, vgaFunctionality, loggerService);
        
        Core.Emulator.OperatingSystem.Dos dos = new Core.Emulator.OperatingSystem.Dos(memory, cpu, keyboardInt16Handler, vgaFunctionality, configuration.CDrive,
            configuration.Exe, configuration.Ems,
            new Dictionary<string, string>() { { "BLASTER", soundBlaster.BlasterString } },
            loggerService);
        
        if (configuration.InitializeDOS is not false) {
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
        
        Core.Emulator.CPU.CfgCpu.CfgCpu cfgCpu = new(memory, cpuState, ioPortDispatcher, callbackHandler, dualPic, emulatorBreakpointsManager, loggerService);

        Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler, biosKeyboardInt9Handler,
            callbackHandler, cpu,
            cfgCpu, cpuState, dos, gravisUltraSound, ioPortDispatcher,
            joystick, keyboard, keyboardInt16Handler, emulatorBreakpointsManager, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler, systemBiosInt15Handler, systemClockInt1AHandler, timer,
            timerInt8Handler,
            vgaCard, videoState, videoInt10Handler, renderer, vgaBios, vgaFunctionality.VgaRom,
            dmaController, soundBlaster.Opl3Fm, softwareMixer, mouse, mouseDriver,
            vgaFunctionality, pauseHandler);
        
        InitializeFunctionHandlers(configuration, machine,  loggerService, reader.ReadGhidraSymbolsFromFileOrCreate(), functionHandler, functionHandlerInExternalInterrupt);

        EmulatorStateSerializer emulatorStateSerializer = new(configuration, memory, cpuState, callbackHandler,
            executionFlowRecorder, functionHandler, loggerService);
        
        ProgramExecutor programExecutor = new(configuration, emulatorBreakpointsManager, emulatorStateSerializer, memory, cpu, cpuState,
            timer, dos, callbackHandler, functionHandler, executionFlowRecorder, pauseHandler, screenPresenter: null,
            loggerService);
        cpu.ErrorOnUninitializedInterruptHandler = false;
        cpuState.Flags.IsDOSBoxCompatible = false;
        return (machine, programExecutor);
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