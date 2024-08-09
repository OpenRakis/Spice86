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
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
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
        MemoryBreakpoints memoryBreakpoints = new();
        IMemory memory = new Memory(memoryBreakpoints, ram, a20gate);
        MachineBreakpoints machineBreakpoints = new(memoryBreakpoints, pauseHandler, memory, cpuState);
        
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
        FunctionHandler functionHandler = new(memory, cpuState, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, cpuState, executionFlowRecorder, loggerService, configuration.DumpDataOnExit is not false);
        Cpu cpu  = new(interruptVectorTable, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, cpuState,
            dualPic, ioPortDispatcher, callbackHandler, machineBreakpoints,
            loggerService, executionFlowRecorder);
        
        // IO devices
        dualPic.InitPortHandlers(ioPortDispatcher);
        DmaController dmaController = new(memory, cpuState, configuration.FailOnUnhandledPort, loggerService);
        dmaController.InitPortHandlers(ioPortDispatcher);

        VideoState videoState = new();
        VgaIoPortHandler videoInt10Handler = new(cpuState, loggerService, videoState, configuration.FailOnUnhandledPort);
        videoInt10Handler.InitPortHandlers(ioPortDispatcher);

        IGui? gui = null;
        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(videoState);
        memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        Renderer renderer = new(videoState, vgaMemory);
        VgaCard vgaCard = new(gui, renderer, loggerService);
        Timer timer = new Timer(configuration, cpuState, loggerService, dualPic);
        timer.InitPortHandlers(ioPortDispatcher);
        Keyboard keyboard = new Keyboard(cpuState, a20gate, dualPic, loggerService, gui, configuration.FailOnUnhandledPort);
        keyboard.InitPortHandlers(ioPortDispatcher);
        Mouse mouse = new Mouse(cpuState, dualPic, gui, configuration.Mouse, loggerService, configuration.FailOnUnhandledPort);
        mouse.InitPortHandlers(ioPortDispatcher);
        Joystick joystick = new Joystick(cpuState, configuration.FailOnUnhandledPort, loggerService);
        joystick.InitPortHandlers(ioPortDispatcher);
        
        SoftwareMixer softwareMixer = new(loggerService);
        
        PcSpeaker pcSpeaker = new PcSpeaker(
            new SoundChannel(softwareMixer, nameof(PcSpeaker)), cpuState,
            loggerService, configuration.FailOnUnhandledPort);
        
        pcSpeaker.InitPortHandlers(ioPortDispatcher);
        
        SoundChannel fmSynthSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster OPL3 FM Synth");
        OPL3FM opl3fm = new OPL3FM(fmSynthSoundChannel, cpuState, configuration.FailOnUnhandledPort, loggerService, pauseHandler);
        opl3fm.InitPortHandlers(ioPortDispatcher);
        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(7, 1, 5, SbType.Sb16);
        SoundChannel pcmSoundChannel = new SoundChannel(softwareMixer, "SoundBlaster PCM");
        SoundBlaster soundBlaster = new SoundBlaster(
            pcmSoundChannel, fmSynthSoundChannel, cpuState, dmaController, dualPic, configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);
        soundBlaster.InitPortHandlers(ioPortDispatcher);
        
        GravisUltraSound gravisUltraSound = new GravisUltraSound(cpuState, configuration.FailOnUnhandledPort, loggerService);
        gravisUltraSound.InitPortHandlers(ioPortDispatcher);
        
        Midi midiDevice = new Midi(softwareMixer, cpuState, pauseHandler, configuration.Mt32RomsPath, configuration.FailOnUnhandledPort, loggerService);
        midiDevice.InitPortHandlers(ioPortDispatcher);

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
            
            var mouseInt33Handler = new MouseInt33Handler(memory, cpu, loggerService, mouseDriver);
            interruptInstaller.InstallInterruptHandler(mouseInt33Handler);

            var mouseIrq12Handler = new BiosMouseInt74Handler(dualPic, memory);
            interruptInstaller.InstallInterruptHandler(mouseIrq12Handler);

            SegmentedAddress mouseDriverAddress = assemblyRoutineInstaller.InstallAssemblyRoutine(mouseDriver);
            mouseIrq12Handler.SetMouseDriverAddress(mouseDriverAddress);
        }
        
        Core.Emulator.CPU.CfgCpu.CfgCpu cfgCpu = new(memory, cpuState, ioPortDispatcher, callbackHandler, dualPic, machineBreakpoints, loggerService);

        Machine machine = new Machine(biosDataArea, biosEquipmentDeterminationInt11Handler, biosKeyboardInt9Handler,
            callbackHandler, interruptInstaller,
            assemblyRoutineInstaller, cpu,
            cfgCpu, cpuState, dos, gravisUltraSound, ioPortDispatcher,
            joystick, keyboard, keyboardInt16Handler, machineBreakpoints, memory, midiDevice, pcSpeaker,
            dualPic, soundBlaster, systemBiosInt12Handler, systemBiosInt15Handler, systemClockInt1AHandler, timer,
            timerInt8Handler,
            vgaCard, videoState, ioPortDispatcher, renderer, vgaBios, vgaFunctionality.VgaRom,
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