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
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Interfaces;

using Xunit;

public class DosFileManagerTests {
    private static readonly string MountPoint = Path.GetFullPath(Path.Combine("Resources", "MountPoint"));

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
        DosFileOperationResult result = dosFileManager.OpenFileOrDevice("C.txt", FileAccessMode.ReadOnly);

        // Assert
        result.Should().BeEquivalentTo(DosFileOperationResult.Value16(3));
        dosFileManager.OpenFiles.ElementAtOrDefault(3)?.Name.Should().Be("C.txt");
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
            AudioEngine = AudioEngine.Dummy,
            DumpDataOnExit = false,
            CDrive = mountPoint,
            RecordedDataDirectory = Path.GetTempPath()
        };
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = new PauseHandler(loggerService);

        RecordedDataReader reader = new(configuration.RecordedDataDirectory!, loggerService);
        ExecutionFlowRecorder executionFlowRecorder = new(configuration.DumpDataOnExit is not false, new());
        State state = new(CpuModel.INTEL_80286);
        ExecutionStateSlice executionStateSlice = new(state) {
            CyclesAllocatedForSlice = 1,
            CyclesLeft = 1
        };
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, configuration.FailOnUnhandledPort);
        A20Gate a20Gate = new(configuration.A20Gate);
        Memory memory = new(memoryBreakpoints, ram, a20Gate,
            initializeResetVector: configuration.InitializeDOS is true);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);
        BiosDataArea biosDataArea =
            new BiosDataArea(memory, conventionalMemorySizeKb: (ushort)Math.Clamp(ram.Size / 1024, 0, 640));
        IOPortHandlerRegistry ioPortHandlerRegistry = new(ioPortDispatcher, state, loggerService, configuration.FailOnUnhandledPort);
        DualPic dualPic = new DualPic(ioPortHandlerRegistry, executionStateSlice, loggerService);

        CallbackHandler callbackHandler = new(state, loggerService);
        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, state);
        FunctionCatalogue functionCatalogue = new FunctionCatalogue(reader.ReadGhidraSymbolsFromFileOrCreate());
        FunctionHandler functionHandler = new(memory, state, executionFlowRecorder, functionCatalogue, false, loggerService);
        FunctionHandler functionHandlerInExternalInterrupt = new(memory, state, executionFlowRecorder, functionCatalogue, false, loggerService);
        Cpu cpu = new(interruptVectorTable, stack,
            functionHandler, functionHandlerInExternalInterrupt, memory, state,
            dualPic, executionStateSlice, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager,
            loggerService, executionFlowRecorder);

        IInstructionExecutor instructionExecutor = cpu;
        IFunctionHandlerProvider functionHandlerProvider = cpu;

        SoftwareMixer softwareMixer = new(loggerService, configuration.AudioEngine);
        PcSpeaker pcSpeaker = new(softwareMixer, state, ioPortDispatcher, pauseHandler, loggerService, dualPic,
            configuration.FailOnUnhandledPort);
        PitTimer pitTimer = new(ioPortHandlerRegistry, dualPic, pcSpeaker, loggerService);
        pcSpeaker.AttachPitControl(pitTimer);

        DmaSystem dmaSystem =
            new(memory, state, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);

        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(5, 1, 5, SbType.Sb16);
        SoundBlaster soundBlaster = new SoundBlaster(ioPortDispatcher, softwareMixer, state, dmaSystem, dualPic,
            configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);

        VgaRom vgaRom = new();
        VgaFunctionality vgaFunctionality = new VgaFunctionality(memory, interruptVectorTable, ioPortDispatcher,
            biosDataArea, vgaRom,
            bootUpInTextMode: configuration.InitializeDOS is true);


        InputEventHub inputEventQueue = new();
        SystemBiosInt15Handler systemBiosInt15Handler = new(configuration, memory,
            functionHandlerProvider, stack, state, a20Gate,
            configuration.InitializeDOS is not false, loggerService);
        Intel8042Controller intel8042Controller = new(
            state, ioPortDispatcher, a20Gate, dualPic,
            configuration.FailOnUnhandledPort, pauseHandler, loggerService, inputEventQueue);
        BiosKeyboardBuffer biosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new(memory, biosDataArea,
            stack, state, functionHandlerProvider, dualPic, systemBiosInt15Handler,
            intel8042Controller, biosKeyboardBuffer, loggerService);
        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(
            memory, biosDataArea, functionHandlerProvider, stack, state, loggerService,
        biosKeyboardInt9Handler.BiosKeyboardBuffer);

        var clock = new Clock(loggerService);

        Dos dos = new Dos(configuration, memory, functionHandlerProvider, stack, state,
            biosKeyboardBuffer, keyboardInt16Handler, biosDataArea,
            vgaFunctionality, new Dictionary<string, string> { { "BLASTER", soundBlaster.BlasterString } },
            clock, loggerService);

        return dos.FileManager;
    }

    [Fact]
    public void OpenFile_ComputesDeviceInfoAndSupportsRelativeSeek() {
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        ushort? handle = null;

        const string fileName = "seektest.bin";

        try {
            DosFileOperationResult openResult = dosFileManager.OpenFileOrDevice(fileName, FileAccessMode.ReadOnly);
            openResult.IsError.Should().BeFalse();
            openResult.Value.Should().NotBeNull();
            handle = (ushort)openResult.Value!.Value;

            dosFileManager.OpenFiles[handle.Value].Should().BeOfType<DosFile>();
            var dosFile = (DosFile)dosFileManager.OpenFiles[handle.Value]!;
            dosFile.DeviceInformation.Should().Be(0x0802);
            dosFile.CanSeek.Should().BeTrue();

            dosFile.Seek(0x200, SeekOrigin.Begin);
            dosFile.Position.Should().Be(0x200);

            DosFileOperationResult seekResult =
                dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Current, handle.Value, -0x1BA);
            seekResult.IsError.Should().BeFalse();
            seekResult.Value.Should().Be(0x46);
            dosFile.Position.Should().Be(0x46);
        } finally {
            if (handle is not null) {
                dosFileManager.CloseFileOrDevice(handle.Value);
            }
        }
    }
}