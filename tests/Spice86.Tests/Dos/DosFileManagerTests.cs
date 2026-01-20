namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;
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
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

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
        TempFile tempFile = new TempFile();
        Configuration configuration = new Configuration() {
            AudioEngine = AudioEngine.Dummy,
            CDrive = mountPoint,
            RecordedDataDirectory = tempFile.Directory,
            Exe = tempFile.Path
        };
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = new PauseHandler(loggerService);
        EmulatorStateSerializationFolder emulatorStateSerializationFolder = 
            new EmulatorStateSerializationFolderFactory(loggerService)
                .ComputeFolder(configuration.Exe, configuration.RecordedDataDirectory);
        EmulationStateDataReader reader = new(emulatorStateSerializationFolder, loggerService);
        State state = new(CpuModel.INTEL_80286);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, configuration.FailOnUnhandledPort);
        A20Gate a20Gate = new(configuration.A20Gate);
        Memory memory = new(memoryBreakpoints, ram, a20Gate,
            initializeResetVector: configuration.InitializeDOS is true);
        IEmulatedClock emulatedClock = new EmulatedClock();
        EmulationLoopScheduler emulationLoopScheduler = new(emulatedClock, loggerService);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);
        
        BiosDataArea biosDataArea =
            new BiosDataArea(memory, conventionalMemorySizeKb: (ushort)Math.Clamp(ram.Size / 1024, 0, 640));

        var dualPic = new DualPic(ioPortDispatcher, state, loggerService, configuration.FailOnUnhandledPort);

        CallbackHandler callbackHandler = new(state, loggerService);
        InterruptVectorTable interruptVectorTable = new(memory);
        Stack stack = new(memory, state);
        FunctionCatalogue functionCatalogue = new FunctionCatalogue(reader.ReadGhidraSymbolsFromFileOrCreate());

        CfgCpu cfgCpu = new(memory, state, ioPortDispatcher, callbackHandler,
            dualPic, emulatorBreakpointsManager, functionCatalogue,
            false, true, loggerService);

        SoftwareMixer softwareMixer = new(loggerService, configuration.AudioEngine);
        PcSpeaker pcSpeaker = new(softwareMixer, state, ioPortDispatcher, pauseHandler, loggerService, emulationLoopScheduler, emulatedClock,
            configuration.FailOnUnhandledPort);
        PitTimer pitTimer = new(ioPortDispatcher, state, dualPic, pcSpeaker, emulationLoopScheduler, emulatedClock, loggerService, configuration.FailOnUnhandledPort);

        pcSpeaker.AttachPitControl(pitTimer);

        DmaBus dmaSystem =
            new(memory, state, ioPortDispatcher, configuration.FailOnUnhandledPort, loggerService);

        var soundBlasterHardwareConfig = new SoundBlasterHardwareConfig(5, 1, 5, SbType.Sb16);
        SoundBlaster soundBlaster = new SoundBlaster(ioPortDispatcher, softwareMixer, state, dmaSystem, dualPic, emulationLoopScheduler, emulatedClock,
            configuration.FailOnUnhandledPort,
            loggerService, soundBlasterHardwareConfig, pauseHandler);

        VgaRom vgaRom = new();
        VgaFunctionality vgaFunctionality = new VgaFunctionality(memory, interruptVectorTable, ioPortDispatcher,
            biosDataArea, vgaRom,
            bootUpInTextMode: configuration.InitializeDOS is true);


        InputEventHub inputEventQueue = new();
        SystemBiosInt15Handler systemBiosInt15Handler = new(configuration, memory,
            cfgCpu, stack, state, a20Gate, biosDataArea, emulationLoopScheduler,
            ioPortDispatcher, loggerService, configuration.InitializeDOS is not false);
        Intel8042Controller intel8042Controller = new(
            state, ioPortDispatcher, a20Gate, dualPic, emulationLoopScheduler,
            configuration.FailOnUnhandledPort, loggerService, inputEventQueue);
        BiosKeyboardBuffer biosKeyboardBuffer = new BiosKeyboardBuffer(memory, biosDataArea);
        BiosKeyboardInt9Handler biosKeyboardInt9Handler = new(memory, biosDataArea,
            stack, state, cfgCpu, dualPic, systemBiosInt15Handler,
            intel8042Controller, biosKeyboardBuffer, loggerService);
        KeyboardInt16Handler keyboardInt16Handler = new KeyboardInt16Handler(
            memory, ioPortDispatcher, biosDataArea, cfgCpu, stack, state, loggerService,
        biosKeyboardInt9Handler.BiosKeyboardBuffer);

        Dos dos = new Dos(configuration, memory, cfgCpu, stack, state,
            biosKeyboardBuffer, keyboardInt16Handler, biosDataArea,
            vgaFunctionality, new Dictionary<string, string> { { "BLASTER", soundBlaster.BlasterString } },
            ioPortDispatcher, loggerService);

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

    [Fact]
    public void FcbParseFilename_SimpleFilename_NoWildcards() {
        // FreeDOS test: Basic filename with extension, no drive
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "TEST.TXT", 9);
        
        byte parseControl = 0x00;
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_NOWILD, "no wildcards");
        bytesAdvanced.Should().Be(8, "TEST.TXT is 8 chars");
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
        fcb.DriveNumber.Should().Be(0, "default drive");
        fcb.CurrentBlock.Should().Be(0, "FreeDOS clears fcb_cublock");
        fcb.RecordSize.Should().Be(0, "FreeDOS clears fcb_recsiz");
    }

    [Fact]
    public void FcbParseFilename_WithDrive_ValidDrive() {
        // FreeDOS test: Filename with drive letter
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "C:FILE.DAT", 11);
        
        byte parseControl = 0x00;
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        bytesAdvanced.Should().Be(10);
        fcb.DriveNumber.Should().Be(3, "C: = drive 3");
        fcb.FileName.Should().Be("FILE    ");
        fcb.FileExtension.Should().Be("DAT");
    }

    [Fact]
    public void FcbParseFilename_InvalidDrive_ContinuesParsing() {
        // FreeDOS test: Invalid drive returns PARSE_RET_BADDRIVE but still parses filename
        // "Undocumented behavior: should keep parsing even if drive specification is invalid"
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "Z:TEST.TXT", 11);
        
        byte parseControl = 0x00;
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_BADDRIVE, "invalid drive Z:");
        bytesAdvanced.Should().Be(10);
        fcb.DriveNumber.Should().Be(26, "Z: = drive 26 even if invalid");
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void FcbParseFilename_Wildcards_Asterisk() {
        // FreeDOS test: Asterisk converts to question marks
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "TEST*.*", 8);
        
        byte parseControl = 0x00;
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_WILD, "wildcards present");
        fcb.FileName.Should().Be("TEST????", "* fills rest with ?");
        fcb.FileExtension.Should().Be("???", "* fills extension with ?");
    }

    [Fact]
    public void FcbParseFilename_Wildcards_QuestionMark() {
        // FreeDOS test: Question marks preserved
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "FI?E.T?T", 9);
        
        byte parseControl = 0x00;
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_WILD);
        fcb.FileName.Should().Be("FI?E    ");
        fcb.FileExtension.Should().Be("T?T");
    }

    [Fact]
    public void FcbParseFilename_SkipLeadingSeparators() {
        // FreeDOS test: PARSE_SKIP_LEAD_SEP flag
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "  :;,=+ \tTEST.TXT", 18);
        
        byte parseControl = DosFcbManager.PARSE_SKIP_LEAD_SEP;
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void FcbParseFilename_WhitespaceAlwaysSkipped() {
        // FreeDOS test: "Undocumented feature, we skip white space anyway"
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "  \t  TEST.TXT", 14);
        
        byte parseControl = 0x00;
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void FcbParseFilename_DotAndDotDot() {
        // FreeDOS test: Special handling for '.' and '..'
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        // Test single dot
        const uint stringAddress1 = 0x1000;
        const uint fcbAddress1 = 0x2000;
        memory.SetZeroTerminatedString(stringAddress1, ".", 2);
        
        byte result1 = fcbManager.ParseFilename(stringAddress1, fcbAddress1, 0x00, out uint bytesAdvanced1);
        DosFileControlBlock fcb1 = new(memory, fcbAddress1);
        
        result1.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        bytesAdvanced1.Should().Be(1);
        fcb1.FileName[0].Should().Be('.');
        fcb1.FileName[1].Should().Be(' ');

        // Test double dot
        const uint stringAddress2 = 0x3000;
        const uint fcbAddress2 = 0x4000;
        memory.SetZeroTerminatedString(stringAddress2, "..", 3);
        
        byte result2 = fcbManager.ParseFilename(stringAddress2, fcbAddress2, 0x00, out uint bytesAdvanced2);
        DosFileControlBlock fcb2 = new(memory, fcbAddress2);
        
        result2.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        bytesAdvanced2.Should().Be(2);
        fcb2.FileName[0].Should().Be('.');
        fcb2.FileName[1].Should().Be('.');
        fcb2.FileName[2].Should().Be(' ');
    }

    [Fact]
    public void FcbParseFilename_NoExtension() {
        // FreeDOS test: Filename without extension
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "TESTFILE", 9);
        
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, 0x00, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        fcb.FileName.Should().Be("TESTFILE");
        fcb.FileExtension.Should().Be("   ", "no extension = spaces");
    }

    [Fact]
    public void FcbParseFilename_UppercaseConversion() {
        // FreeDOS test: Lowercase converted to uppercase (DosUpFChar)
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        memory.SetZeroTerminatedString(stringAddress, "test.txt", 9);
        
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, 0x00, out uint bytesAdvanced);

        DosFileControlBlock fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void FcbParseFilename_ParseControlFlags() {
        // FreeDOS test: PARSE_BLNK_FNAME and PARSE_BLNK_FEXT flags
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);
        Ram ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new(), ram, new A20Gate());
        DosDriveManager driveManager = new DosDriveManager(Substitute.For<ILoggerService>(), MountPoint, null);
        DosFcbManager fcbManager = new DosFcbManager(memory, dosFileManager, driveManager, Substitute.For<ILoggerService>());

        // First, set some initial values in FCB
        const uint fcbAddress = 0x2000;
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.FileName = "OLDNAME ";
        fcb.FileExtension = "OLD";

        const uint stringAddress = 0x1000;
        memory.SetZeroTerminatedString(stringAddress, "TEST.TXT", 9);
        
        // With PARSE_BLNK_FNAME | PARSE_BLNK_FEXT, should NOT clear the fields
        byte parseControl = (byte)(DosFcbManager.PARSE_BLNK_FNAME | DosFcbManager.PARSE_BLNK_FEXT);
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, parseControl, out _);

        fcb = new(memory, fcbAddress);
        result.Should().Be(DosFcbManager.PARSE_RET_NOWILD);
        fcb.FileName.Should().Be("TEST    ", "filename should still be parsed");
        fcb.FileExtension.Should().Be("TXT", "extension should still be parsed");
    }
}


