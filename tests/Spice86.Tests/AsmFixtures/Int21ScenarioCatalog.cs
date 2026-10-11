namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Utils;
using Spice86.Tests.Dos.FileSystem;
using Spice86.Tests.Utility;
using Spice86.ViewModels.Services;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Scenarios of the DOS INT 21h integration programs under Resources/DosInt21Tests. Every scenario copies
/// its program as TEST.COM into a fresh per-run working directory mounted as C:, so the discovery run and
/// the generated execution start from identical host files, disk images, redirected input and keyboard
/// events, and a program that creates or modifies host files never pollutes another run. The stable
/// identity of a scenario is its resource name, never the shared TEST.COM file name, so two different
/// programs copied to TEST.COM never share a golden key. Every program reports through the shared test
/// result ports 0x999 and 0x998, and the oracle keeps the original success/failure and detail-byte
/// checks in both execution modes.
/// </summary>
internal static class Int21ScenarioCatalog {
    private const string ResourceSubDirectory = "DosInt21Tests";
    private const string TestProgramFileName = "TEST.COM";
    private const string TestPortResultsObservation = "testPortResults";
    private const byte TestPortSuccess = 0x00;
    private const byte TestPortFailure = 0xFF;
    private const long MaxCycles = 100000L;
    private const long KeyboardTestInstructionsPerSecond = 1000;
    private const long KeyInjectionCycleCount = 50;
    private const int CtrlPrintScreenTranslation = 0x10;

    private static readonly byte[] DeviceStatusReportResponse = {
        0x1B, (byte)'[', (byte)'5', (byte)';', (byte)'1', (byte)'0', (byte)'R', 0x0D
    };

    private static readonly byte[] BufferedStdinHelloCr = { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x0D };

    private static readonly Action<TestIoPortHandler> NoDetailAssertions = _ => { };

    /// <summary>Every INT 21h scenario, in the order of the resources they execute.</summary>
    public static IReadOnlyList<AsmScenario> Scenarios => [
        Simple("dbcs_lead_byte_table_al0.com"),
        Simple("dbcs_lead_byte_table_invalid_al.com"),
        Simple("cds_initialized_known_location.com"),
        Simple("dbcs_table_private_area.com"),
        Simple("psp_parent_command_com.com"),
        Simple("environment_block_not_corrupted.com"),
        Simple("environment_block_contains_program_path.com"),
        Simple("child_return_code_initial.com"),
        Simple("child_return_code_subsequent_zero.com"),
        Simple("int20_terminates.com"),
        Simple("create_new_psp_valid_copy.com"),
        Simple("create_child_psp_valid.com"),
        Simple("program_arguments_access_argv.com"),
        Simple("program_arguments_print_argv0.com"),
        StandardFileHandlesInherited(),
        Simple("select_default_drive.com"),
        Simple("allocation_info_default_drive.com"),
        CdRomImageRead(),
        FloppyImageRead(),
        SignedSeekOffset(),
        Simple("set_handle_count_updates_psp.com"),
        ReadOnlyFileAttributes(),
        SetFileAttributesRoundTrip(),
        Simple("control_break_roundtrip.com"),
        StdinStatusDetectsCtrlC(),
        StdinStatusDetectsCtrlBreak(),
        SingleStdinRead("stdin_read_ah07.com"),
        SingleStdinRead("stdin_read_ah08.com"),
        SingleStdinRead("stdin_read_ah01.com"),
        BufferedStdinRead(),
        KeyboardReassignmentReset(),
        FlushInputClearsStuffahead(),
        DeviceStatusReportCursorPosition(),
        FindFirstWildcard(),
        FindFirstShortFilename(),
        Umb("umb_chain_link.com"),
        ProgramScenario("umb_ems_size", "umb_ems_size.com", new Int21RunSetup {
            EnableXms = true,
            EnableEms = true
        }, NoDetailAssertions),
        ProgramScenario("umb_disabled", "umb_disabled.com", new Int21RunSetup {
            EnableXms = true,
            EnableUmb = false
        }, NoDetailAssertions),
        Umb("umb_alloc_strategy.com"),
        WithHandleTestFiles("file_handles_are_lowest_free_jft_slot.com"),
        WithHandleTestFiles("child_psp_inherits_jft_slots.com"),
        ProgramScenario("crack_close_handle8_hook", "crack_close_handle8_hook.com",
            new Int21RunSetup {
                CreateHostFiles = CreateHandleTestFiles
            }, testHandler => testHandler.Details.Should().Equal((byte)19)),
        ProgramScenario("child_exec_terminate_releases_handles", "child_exec_terminate_releases_handles.com",
            new Int21RunSetup {
                CreateHostFiles = directory => {
                    CreateHandleTestFiles(directory);
                    CopyResourceFile("child_opens_files_without_closing.com", directory, "CHILD.COM");
                },
                AdditionalResourceDependencies = [
                    $"{ResourceSubDirectory}/child_opens_files_without_closing.com"
                ]
            }, NoDetailAssertions),
        Simple("terminate_after_create_child_psp_restores_parent_stack.com"),
        WithHandleTestFiles("set_handle_count_relocates_jft.com")
    ];

    private static AsmScenario Simple(string programFileName) {
        return ProgramScenario(ProgramName(programFileName), programFileName, Int21RunSetup.None,
            NoDetailAssertions);
    }

    /// <summary>The program exercises DOS upper memory blocks, so the run provides XMS.</summary>
    private static AsmScenario Umb(string programFileName) {
        return ProgramScenario(ProgramName(programFileName), programFileName, new Int21RunSetup {
            EnableXms = true
        }, NoDetailAssertions);
    }

    /// <summary>The program checks DOS file handles against the A.TXT to D.TXT files the run creates.</summary>
    private static AsmScenario WithHandleTestFiles(string programFileName) {
        return ProgramScenario(ProgramName(programFileName), programFileName, new Int21RunSetup {
            CreateHostFiles = CreateHandleTestFiles
        }, NoDetailAssertions);
    }

    /// <summary>The program reads its standard handles 0, 1 and 2 from the parent PSP and reports each
    /// handle value as a detail byte, so the first three details are 0, 1 and 2.</summary>
    private static AsmScenario StandardFileHandlesInherited() {
        return ProgramScenario("standard_file_handles_inherited", "standard_file_handles_inherited.com",
            Int21RunSetup.None, testHandler => testHandler.Details.Take(3).Should()
                .Equal(new byte[] { 0, 1, 2 }, "handles 0, 1 and 2 are inherited from the parent PSP"));
    }

    /// <summary>The program opens README.TXT on a mounted CD-ROM image and reads its content, so the run
    /// must mount a freshly built ISO as drive D: before the program starts.</summary>
    private static AsmScenario CdRomImageRead() {
        return ProgramScenario("read_cd_image_file", "read_cd_image_file.com", new Int21RunSetup {
            CreateHostFiles = CreateIsoImageFile,
            CdRomImages = directory => new Dictionary<char, string> { ['D'] = Path.Join(directory, "disc.iso") }
        }, NoDetailAssertions);
    }

    /// <summary>The program opens SUBDIR\HELLO.TXT on a mounted floppy image, so the run must mount a
    /// freshly built FAT12 image as drive A: before the program starts.</summary>
    private static AsmScenario FloppyImageRead() {
        return ProgramScenario("read_floppy_image_file", "read_floppy_image_file.com", new Int21RunSetup {
            CreateHostFiles = CreateFloppyImageFile,
            FloppyImages = directory => new Dictionary<char, string> { ['A'] = Path.Join(directory, "disk.img") }
        }, NoDetailAssertions);
    }

    /// <summary>The program seeks with a CX:DX offset that is negative when treated as a signed value, so
    /// the run needs the patterned SEEKTEST.BIN host file to seek in.</summary>
    private static AsmScenario SignedSeekOffset() {
        return ProgramScenario("move_file_pointer_signed_offset", "move_file_pointer_signed_offset.com",
            new Int21RunSetup {
                CreateHostFiles = directory => {
                    byte[] testData = Enumerable.Range(0, 0x400).Select(i => (byte)i).ToArray();
                    File.WriteAllBytes(Path.Join(directory, "SEEKTEST.BIN"), testData);
                }
            }, NoDetailAssertions);
    }

    /// <summary>The program checks the DOS ReadOnly attribute bit of a read-only file and reports CL, the
    /// attributes it read, as its first detail byte. The archive bit depends on the host file system, so
    /// the oracle checks it on Windows hosts only, where the archive bit of a read-only file persists.</summary>
    private static AsmScenario ReadOnlyFileAttributes() {
        return ProgramScenario("file_attributes_readonly", "file_attributes_readonly.com", new Int21RunSetup {
            CreateHostFiles = directory => {
                string testFilePath = Path.Join(directory, "TESTATTR.TXT");
                File.WriteAllText(testFilePath, "test");
                File.SetAttributes(testFilePath, FileAttributes.ReadOnly | FileAttributes.Archive);
            }
        }, testHandler => {
            testHandler.Details.Should().NotBeEmpty("the program reports the read attributes CL as a detail byte");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                (testHandler.Details[0] & 0x20).Should().Be(0x20,
                    "Windows hosts persist the DOS archive bit of a read-only file");
            }
        });
    }

    /// <summary>The program sets file attributes with INT 21h and reads them back, so the run needs the
    /// pre-attributeed SETATR.TXT host file.</summary>
    private static AsmScenario SetFileAttributesRoundTrip() {
        return ProgramScenario("set_file_attributes_roundtrip", "set_file_attributes_roundtrip.com",
            new Int21RunSetup {
                CreateHostFiles = directory => {
                    string testFilePath = Path.Join(directory, "SETATR.TXT");
                    File.WriteAllText(testFilePath, "test");
                    File.SetAttributes(testFilePath, FileAttributes.Archive);
                }
            }, NoDetailAssertions);
    }

    /// <summary>The program polls standard input with break checking enabled; the run presses Ctrl-C into
    /// the headless GUI while the program runs, and the program must detect it and invoke INT 23h.</summary>
    private static AsmScenario StdinStatusDetectsCtrlC() {
        return ProgramScenario("check_stdin_status_ctrl_c", "check_stdin_status_ctrl_break.com",
            KeyboardSetup(SimulateCtrlC), NoDetailAssertions);
    }

    /// <summary>Same program as <see cref="StdinStatusDetectsCtrlC"/>, but the run presses Ctrl-Break, so it
    /// is a distinct scenario with its own identity and goldens.</summary>
    private static AsmScenario StdinStatusDetectsCtrlBreak() {
        return ProgramScenario("check_stdin_status_ctrl_break", "check_stdin_status_ctrl_break.com",
            KeyboardSetup(SimulateCtrlBreak), NoDetailAssertions);
    }

    /// <summary>The program reads a single character through its standard input handle, which the run
    /// redirects to a stream holding one 0x41 byte.</summary>
    private static AsmScenario SingleStdinRead(string programFileName) {
        return ProgramScenario(ProgramName(programFileName), programFileName, new Int21RunSetup {
            ConfigureMachine = machine => RedirectStdin(machine, new byte[] { 0x41 })
        }, testHandler => testHandler.Results.Should().Contain(TestPortSuccess,
            $"{programFileName} should read 0x41 from the redirected STDIN handle"));
    }

    /// <summary>The program performs a buffered read through its standard input handle, which the run
    /// redirects to a stream holding the line "Hello" followed by a carriage return.</summary>
    private static AsmScenario BufferedStdinRead() {
        return ProgramScenario("stdin_buffered_read_ah0a", "stdin_buffered_read_ah0a.com", new Int21RunSetup {
            ConfigureMachine = machine => RedirectStdin(machine, BufferedStdinHelloCr)
        }, testHandler => testHandler.Results.Should().Contain(TestPortSuccess,
            "stdin_buffered_read_ah0a.com should read from the redirected STDIN handle"));
    }

    /// <summary>The program resets the keyboard reassignment table and reads back Ctrl+PrintScreen, which
    /// the default mapping translates to Ctrl+P (0x10).</summary>
    private static AsmScenario KeyboardReassignmentReset() {
        return ProgramScenario("keyboard_reassignment_reset", "keyboard_reassignment_reset.com",
            Int21RunSetup.None, testHandler => {
                testHandler.Details.Should().HaveCount(1, "should read exactly one character from Ctrl+PrintScreen");
                testHandler.Details[0].Should().Be(CtrlPrintScreenTranslation,
                    "Ctrl+PrintScreen should be translated to Ctrl+P by the default key mapping");
            });
    }

    /// <summary>The program injects a Device Status Report into the stuffahead buffer, flushes input with
    /// INT 21h AH=0Ch, stuffs a 'Z' and must read exactly that 'Z' back.</summary>
    private static AsmScenario FlushInputClearsStuffahead() {
        return ProgramScenario("flush_input_clears_stuffahead", "flush_input_clears_stuffahead.com",
            Int21RunSetup.None, testHandler => {
                testHandler.Details.Should().HaveCount(1, "should read exactly one character after flush");
                testHandler.Details[0].Should().Be((byte)'Z',
                    "flush should have discarded the DSR response; next read should get 'Z' from the buffer");
            });
    }

    /// <summary>The program positions the cursor and reads the Device Status Report bytes back through
    /// standard input, which must be exactly ESC[5;10R plus carriage return.</summary>
    private static AsmScenario DeviceStatusReportCursorPosition() {
        return ProgramScenario("device_status_report", "device_status_report.com", Int21RunSetup.None,
            testHandler => testHandler.Details.Should().Equal(DeviceStatusReportResponse,
                "DSR response should be ESC[5;10R\\r for cursor at row 4, col 9"));
    }

    /// <summary>The program searches with wildcards over a host directory holding the full pattern matrix,
    /// so every DOS wildcard rule is checked against real directory listings in both modes.</summary>
    private static AsmScenario FindFirstWildcard() {
        string[] files = [
            "README.TXT", "READ.TXT", "RE.TXT", "README",
            "FILE.EXE", "FILE.E", "FILE", "FILE.BIN", "FILE.T", "FILE.TXT",
            "A.TXT", "AB.TXT", "ABC.TXT", "AB", "A", "A.T",
            "ABCDE.TXT", "ABCDEF.TXT",
            "FOO", "FOO.TXT", "FOOBAR.BAZ",
            "MYPROG.COM", "MYPROG.XOM", "MYTEST.TXT",
            "HIGHSCOR.DAT"
        ];
        return ProgramScenario("find_first_wildcard", "find_first_wildcard.com", new Int21RunSetup {
            CreateHostFiles = directory => WriteTestFiles(directory, files)
        }, NoDetailAssertions);
    }

    /// <summary>The program reads the 8.3 short names DOS generates for long and mixed-case file names,
    /// so every run needs the same freshly created set of long-named host files.</summary>
    private static AsmScenario FindFirstShortFilename() {
        string[] files = [
            "README.TXT",
            "VeryLongFileName.txt",
            "LongDocument_Alpha.doc",
            "LongDocument_Beta.doc",
            "LongDocument_Gamma.doc",
            "readme.text",
            "My File.txt",
            "VeryLongFilenameNoExt",
            "ABCDEFGH.TXT",
            "XYZDEFGHI.TXT"
        ];
        return ProgramScenario("find_first_short_filename", "find_first_short_filename.com", new Int21RunSetup {
            CreateHostFiles = directory => WriteTestFiles(directory, files)
        }, NoDetailAssertions);
    }

    private static AsmScenario ProgramScenario(string identity, string programFileName, Int21RunSetup setup,
        Action<TestIoPortHandler> assertDetails) {
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            setup.CreateHostFiles(directory);
            string programPath = CopyResourceFile(programFileName, directory);
            AsmRunSettings settings = new() {
                BinName = programPath,
                ScenarioName = identity,
                MaxCycles = MaxCycles,
                InstallInterruptVectors = true,
                EnableA20Gate = true,
                CDrive = directory,
                InstructionTimeScale = setup.InstructionTimeScale,
                EnableXms = setup.EnableXms,
                EnableEms = setup.EnableEms,
                EnableUmb = setup.EnableUmb,
                MountedCdRomImages = setup.CdRomImages(directory),
                MountedFloppyImages = setup.FloppyImages(directory),
                HeadlessKeyInjections = setup.KeyInjections,
                ConfigureMachine = machine => {
                    RegisterTestPortHandler(scope, machine);
                    setup.ConfigureMachine(machine);
                }
            };
            return new AsmFixture(settings, [], TestPortOracle(programFileName, assertDetails));
        }, [$"{ResourceSubDirectory}/{programFileName}", .. setup.AdditionalResourceDependencies]) {
            PrimaryResourceFileName = programFileName
        };
    }

    private static Int21RunSetup KeyboardSetup(Action<HeadlessGui> injectKeys) {
        return new Int21RunSetup {
            InstructionTimeScale = KeyboardTestInstructionsPerSecond,
            KeyInjections = [new HeadlessKeyInjection(KeyInjectionCycleCount, injectKeys)]
        };
    }

    private static Action<AsmFixtureResult> TestPortOracle(string programFileName,
        Action<TestIoPortHandler> assertDetails) {
        return result => {
            TestIoPortHandler testHandler = result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation);
            testHandler.Results.Should().Contain(TestPortSuccess, $"{programFileName} must report success");
            testHandler.Results.Should().NotContain(TestPortFailure, $"{programFileName} must not report failure");
            assertDetails(testHandler);
        };
    }

    private static void RegisterTestPortHandler(AsmRunScope scope, Machine machine) {
        scope.Register(TestPortResultsObservation,
            new TestIoPortHandler(machine.CpuState, Substitute.For<ILogger>(), machine.IoPortDispatcher));
    }

    private static void RedirectStdin(Machine machine, byte[] data) {
        MemoryStream stream = new(data);
        DosFile stdinFile = new("STDIN", 0, stream);
        const byte systemFileTableIndex = 10;
        machine.Dos.FileManager.OpenFiles[systemFileTableIndex] = stdinFile;
        ushort pspSegment = machine.Dos.DosSwappableDataArea.CurrentProgramSegmentPrefix;
        DosProgramSegmentPrefix psp = new(machine.Memory, MemoryUtils.ToPhysicalAddress(pspSegment, 0));
        uint stdinEntryAddress = MemoryUtils.ToPhysicalAddress(psp.FileTableAddress.Segment, psp.FileTableAddress.Offset);
        machine.Memory.UInt8[stdinEntryAddress] = systemFileTableIndex;
    }

    private static void SimulateCtrlC(HeadlessGui gui) {
        gui.SimulateKeyPress(PhysicalKey.ControlLeft);
        gui.SimulateKeyPress(PhysicalKey.C);
        gui.SimulateKeyRelease(PhysicalKey.C);
        gui.SimulateKeyRelease(PhysicalKey.ControlLeft);
    }

    private static void SimulateCtrlBreak(HeadlessGui gui) {
        gui.SimulateKeyPress(PhysicalKey.ControlLeft);
        gui.SimulateKeyPress(PhysicalKey.Pause);
        gui.SimulateKeyRelease(PhysicalKey.ControlLeft);
    }

    private static void CreateIsoImageFile(string directory) {
        string sourcePath = Path.Join(directory, "iso-source");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Join(sourcePath, "README.TXT"), "SPICE86!");
        using VirtualIsoImage image = new(sourcePath, "SPICE86");
        byte[] isoBytes = new byte[image.TotalSectors * 2048];
        for (int lba = 0; lba < image.TotalSectors; lba++) {
            int offset = lba * 2048;
            int bytesRead = image.Read(lba, isoBytes.AsSpan(offset, 2048), CdSectorMode.CookedData2048);
            if (bytesRead != 2048) {
                throw new InvalidOperationException("Expected a complete cooked ISO sector.");
            }
        }
        File.WriteAllBytes(Path.Join(directory, "disc.iso"), isoBytes);
    }

    private static void CreateFloppyImageFile(string directory) {
        byte[] image = new Fat12ImageBuilder()
            .WithFile("SUBDIR\\HELLO.TXT", Encoding.ASCII.GetBytes("FLOPPY!"))
            .Build();
        File.WriteAllBytes(Path.Join(directory, "disk.img"), image);
    }

    private static void WriteTestFiles(string directory, IEnumerable<string> fileNames) {
        foreach (string fileName in fileNames) {
            File.WriteAllText(Path.Join(directory, fileName), "test");
        }
    }

    private static void CreateHandleTestFiles(string directory) {
        foreach (string name in new[] { "A", "B", "C", "D" }) {
            File.WriteAllText(Path.Join(directory, $"{name}.TXT"), name);
        }
    }

    private static string CopyResourceFile(string programFileName, string targetDirectory) {
        return CopyResourceFile(programFileName, targetDirectory, TestProgramFileName);
    }

    private static string CopyResourceFile(string resourceFileName, string targetDirectory, string targetFileName) {
        string source = Path.Join(AppContext.BaseDirectory, "Resources", ResourceSubDirectory, resourceFileName);
        string target = Path.Join(targetDirectory, targetFileName);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    private static string ProgramName(string programFileName) {
        return Path.GetFileNameWithoutExtension(programFileName);
    }

    /// <summary>Per-scenario run configuration on top of the shared TEST.COM setup: extra host files,
    /// redirected standard input, mounted disk images, keyboard events and the instruction time scale.</summary>
    private sealed class Int21RunSetup {
        public static readonly Int21RunSetup None = new();

        /// <summary>Creates the host files the program needs, in the run's working directory.</summary>
        public Action<string> CreateHostFiles { get; init; } = _ => { };

        /// <summary>Configures the fresh machine before the program runs, e.g. redirected input.</summary>
        public Action<Machine> ConfigureMachine { get; init; } = _ => { };

        /// <summary>Maps the run's working directory to the ISO images mounted as CD-ROM drives.</summary>
        public Func<string, IReadOnlyDictionary<char, string>> CdRomImages { get; init; } =
            _ => AsmRunSettings.NoMountedImages;

        /// <summary>Maps the run's working directory to the FAT images mounted as floppy drives.</summary>
        public Func<string, IReadOnlyDictionary<char, string>> FloppyImages { get; init; } =
            _ => AsmRunSettings.NoMountedImages;

        /// <summary>Keyboard events injected into the headless GUI while the program runs.</summary>
        public IReadOnlyList<HeadlessKeyInjection> KeyInjections { get; init; } = [];

        /// <summary>Instruction time scale of the run, or null for the deterministic default.</summary>
        public long? InstructionTimeScale { get; init; }

        /// <summary>Whether the run provides XMS.</summary>
        public bool EnableXms { get; init; }

        /// <summary>Whether the run provides EMS.</summary>
        public bool EnableEms { get; init; }

        /// <summary>Whether the run provides DOS upper memory blocks.</summary>
        public bool EnableUmb { get; init; } = true;

        /// <summary>Extra resource files the scenario needs, relative to the resources root, beyond the
        /// main program it runs.</summary>
        public IReadOnlyList<string> AdditionalResourceDependencies { get; init; } = [];
    }
}
