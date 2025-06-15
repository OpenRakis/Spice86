namespace Spice86.Tests.Dos;
using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public class DosFileManagerTests {
    private static readonly string MountPoint = Path.GetFullPath(@"Resources\MountPoint");

    [Theory]
    [InlineData(@"\FoO", "FOO")]
    [InlineData(@"/FOo", "FOO")]
    [InlineData(@"/fOO/", "FOO")]
    [InlineData(@"/Foo\", "FOO")]
    [InlineData(@"\FoO\", "FOO")]
    [InlineData(@"C:\FoO\BAR\", @"FOO\BAR")]
    [InlineData(@"C:\", "")]
    public void AbsolutePaths(string dosPath, string expected) {
        // Arrange
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);

        // Act
        dosFileManager.SetCurrentDir(dosPath);

        // Assert
        DosFileOperationResult result = dosFileManager.GetCurrentDir(0x0, out string currentDir);
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void CanOpenFileBeginningWithC() {
        // Arrange
        DosFileManager dosFileManager = ArrangeDosFileManager(@$"{MountPoint}\foo\bar");

        // Act
        DosFileOperationResult result = dosFileManager.OpenFile("C.txt", FileAccessMode.WriteOnly);

        // Assert
        result.Should().BeEquivalentTo(DosFileOperationResult.Value16(0));
        dosFileManager.OpenFiles.ElementAtOrDefault(0)?.Name.Should().Be("C.txt");
    }

    [Theory]
    [InlineData(@"foo", "FOO")]
    [InlineData(@"foo/", "FOO")]
    [InlineData(@"foo\", "FOO")]
    [InlineData(@".\FOO", "FOO")]
    [InlineData(@"C:FOO", "FOO")]
    [InlineData(@"C:FOO\", "FOO")]
    [InlineData(@"C:FOO/", "FOO")]
    [InlineData(@"C:foo\bar", @"FOO\BAR")]
    [InlineData(@"../foo/BAR", @"FOO\BAR")]
    [InlineData(@"..\foo\BAR", @"FOO\BAR")]
    [InlineData(@"./FOO/BAR", @"FOO\BAR")]
    public void RelativePaths(string dosPath, string expected) {
        // Arrange
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);

        // Act
        dosFileManager.SetCurrentDir(dosPath);

        // Assert
        DosFileOperationResult result = dosFileManager.GetCurrentDir(0x0, out string currentDir);
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo(expected);
    }

    private static DosFileManager ArrangeDosFileManager(string mountPoint) {
        Configuration configuration = new Configuration() {
            DumpDataOnExit = false,
            CDrive = mountPoint
        };
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = new PauseHandler(loggerService);

        RecordedDataReader reader = new(configuration.RecordedDataDirectory, loggerService);
        ExecutionFlowRecorder executionFlowRecorder =
            reader.ReadExecutionFlowRecorderFromFileOrCreate(configuration.DumpDataOnExit is not false);
        State state = new();
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state);
        IOPortDispatcher ioPortDispatcher = new(emulatorBreakpointsManager.IoReadWriteBreakpoints, state, loggerService, configuration.FailOnUnhandledPort);
        A20Gate a20Gate = new(configuration.A20Gate);
        Memory memory = new(emulatorBreakpointsManager.MemoryReadWriteBreakpoints, ram, a20Gate,
            initializeResetVector: configuration.InitializeDOS is true);
        var biosDataArea =
            new BiosDataArea(memory, conventionalMemorySizeKb: (ushort)Math.Clamp(ram.Size / 1024, 0, 640));
        var dualPic = new DualPic(state, ioPortDispatcher, configuration.FailOnUnhandledPort,
            configuration.InitializeDOS is false, loggerService);

        CallbackHandler callbackHandler = new(state, loggerService);
        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, state);
        FunctionCatalogue functionCatalogue = new FunctionCatalogue(reader.ReadGhidraSymbolsFromFileOrCreate());
        FunctionHandler functionHandler = new(memory, state, executionFlowRecorder, functionCatalogue, loggerService);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, state, executionFlowRecorder, functionCatalogue, loggerService);
        Cpu cpu = new(interruptVectorTable, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, state,
            dualPic, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager,
            loggerService, executionFlowRecorder);

        IInstructionExecutor instructionExecutor = cpu;
        IFunctionHandlerProvider functionHandlerProvider =  cpu;

        // IO devices
        Timer timer = new Timer(configuration, state, ioPortDispatcher,
            new CounterConfiguratorFactory(configuration, state, pauseHandler, loggerService), loggerService, dualPic);
        TimerInt8Handler timerInt8Handler =
            new TimerInt8Handler(memory, functionHandlerProvider, stack, state, dualPic, timer, biosDataArea, loggerService);

        DmaController dmaController =
            new(memory, state, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);

        SoftwareMixer softwareMixer = new(loggerService, configuration.AudioEngine);
        Midi midiDevice = new Midi(configuration, softwareMixer, state, ioPortDispatcher, pauseHandler, configuration.Mt32RomsPath,
            configuration.FailOnUnhandledPort, loggerService);

        PcSpeaker pcSpeaker = new PcSpeaker(softwareMixer, state, timer.GetCounter(2), ioPortDispatcher, pauseHandler, loggerService,
            configuration.FailOnUnhandledPort);

        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(5, 1, 5, SbType.Sb16);
        SoundBlaster soundBlaster = new SoundBlaster(ioPortDispatcher, softwareMixer, state, dmaController, dualPic,
            configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);

        VgaRom vgaRom = new();
        VgaFunctionality vgaFunctionality = new VgaFunctionality(memory, interruptVectorTable, ioPortDispatcher,
            biosDataArea, vgaRom,
            bootUpInTextMode: configuration.InitializeDOS is true);

        Keyboard keyboard = new Keyboard(state, ioPortDispatcher, a20Gate, dualPic, loggerService,
            null, configuration.FailOnUnhandledPort);
        BiosKeyboardBuffer biosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler =
            new BiosKeyboardInt9Handler(memory, functionHandlerProvider, stack,
            state, dualPic, keyboard, biosKeyboardBuffer, loggerService);

        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(memory, biosDataArea, functionHandlerProvider, stack, state, loggerService,
            biosKeyboardInt9Handler.BiosKeyboardBuffer);

        EmulationLoop emulationLoop = new EmulationLoop(loggerService,
            functionHandler, instructionExecutor, state, timer,
            emulatorBreakpointsManager, dmaController, pauseHandler);

        Dos dos = new Dos(memory, functionHandlerProvider, stack, state,
            new EmulationLoopRecalls(interruptVectorTable, state, stack, emulationLoop),
            biosKeyboardBuffer, keyboardInt16Handler, biosDataArea,
            vgaFunctionality, configuration.CDrive,
            configuration.Exe, configuration.InitializeDOS is not false, configuration.Ems,
            new Dictionary<string, string> { { "BLASTER", soundBlaster.BlasterString } },
            loggerService);

        DosDriveManager dosDriveManager = new(configuration.CDrive, configuration.Exe);

        return new DosFileManager(memory, new DosStringDecoder(memory, state), dosDriveManager, loggerService, dos.Devices);
    }
}
