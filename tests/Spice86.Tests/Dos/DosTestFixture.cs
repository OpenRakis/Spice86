namespace Spice86.Tests.Dos;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

/// <summary>
/// Shared test fixture for DOS components (FileManager, FcbManager, etc.)
/// Provides a fully wired DOS environment for testing.
/// </summary>
public class DosTestFixture {
    public DosFileManager DosFileManager { get; }
    public DosFcbManager DosFcbManager { get; }
    public Memory Memory { get; }
    public ILoggerService LoggerService { get; }
    public Dos Dos { get; }

    public DosTestFixture(string mountPoint) {
        Configuration configuration = new Configuration() {
            AudioEngine = AudioEngine.Dummy,
            CDrive = mountPoint,
            RecordedDataDirectory = Path.GetTempPath()
        };
        
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        LoggerService = new LoggerService();
        IPauseHandler pauseHandler = new PauseHandler(LoggerService);

        State state = new(CpuModel.INTEL_80286);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, LoggerService, configuration.FailOnUnhandledPort);
        A20Gate a20Gate = new(configuration.A20Gate);
        Memory = new(memoryBreakpoints, ram, a20Gate,
            initializeResetVector: configuration.InitializeDOS is true);
        IEmulatedClock emulatedClock = new EmulatedClock();
        EmulationLoopScheduler emulationLoopScheduler = new(emulatedClock, LoggerService);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state, Memory, memoryBreakpoints, ioBreakpoints);
        
        BiosDataArea biosDataArea =
            new BiosDataArea(Memory, conventionalMemorySizeKb: (ushort)Math.Clamp(ram.Size / 1024, 0, 640));

        DualPic dualPic = new(ioPortDispatcher, state, LoggerService, configuration.FailOnUnhandledPort);

        CallbackHandler callbackHandler = new(state, LoggerService);
        InterruptVectorTable interruptVectorTable = new(Memory);
        Stack stack = new(Memory, state);
        FunctionCatalogue functionCatalogue = new FunctionCatalogue();

        CfgCpu cfgCpu = new(Memory, state, ioPortDispatcher, callbackHandler,
            dualPic, emulatorBreakpointsManager, functionCatalogue,
            false, true, LoggerService);

        VgaRom vgaRom = new();
        VgaFunctionality vgaFunctionality = new VgaFunctionality(Memory, interruptVectorTable, ioPortDispatcher,
            biosDataArea, vgaRom,
            bootUpInTextMode: configuration.InitializeDOS is true);

        InputEventHub inputEventQueue = new();
        SystemBiosInt15Handler systemBiosInt15Handler = new(configuration, Memory,
            cfgCpu, stack, state, a20Gate, biosDataArea, emulationLoopScheduler,
            ioPortDispatcher, LoggerService, configuration.InitializeDOS is not false);
        Intel8042Controller intel8042Controller = new(
            state, ioPortDispatcher, a20Gate, dualPic, emulationLoopScheduler,
            configuration.FailOnUnhandledPort, LoggerService, inputEventQueue);
        BiosKeyboardBuffer biosKeyboardBuffer = new BiosKeyboardBuffer(Memory, biosDataArea);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new(Memory, biosDataArea,
            stack, state, cfgCpu, dualPic, systemBiosInt15Handler,
            intel8042Controller, biosKeyboardBuffer, LoggerService);
        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(
            Memory, ioPortDispatcher, biosDataArea, cfgCpu, stack, state, LoggerService,
            biosKeyboardInt9Handler.BiosKeyboardBuffer);

        Dos = new Dos(configuration, Memory, cfgCpu, stack, state,
            biosKeyboardBuffer, keyboardInt16Handler, biosDataArea,
            vgaFunctionality, new Dictionary<string, string> { { "BLASTER", "" } },
            ioPortDispatcher, LoggerService);

        DosFileManager = Dos.FileManager;
        DosFcbManager = Dos.FcbManager;
    }
}
