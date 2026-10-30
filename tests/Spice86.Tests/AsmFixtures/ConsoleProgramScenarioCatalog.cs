namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Spice86.Tests.Dos;

using System.Text;

using static Spice86.Tests.AsmFixtures.DosProgramRun;

/// <summary>
/// Scenarios of the console and keyboard programs committed under
/// <c>Resources/DosConsoleProgramTests</c>: the INT 21h console IOCTL subfunctions, the console input and
/// break-check services and the enhanced INT 16h keystroke services. Each program runs in its own
/// directory mounted as C:, so the redirected files and preloaded keys of one run never reach another,
/// and the same oracle checks the reported status byte, the video cell or the redirected output in the
/// interpreted and in the generated mode.
/// </summary>
internal static class ConsoleProgramScenarioCatalog {
    private const string ProgramDirectory = "DosConsoleProgramTests";
    private const ushort CtrlCBiosKeyCode = 0x0003;
    private const byte CharacterDeviceBit = 0x80;
    private const byte CarryFlagReported = 0x01;
    private const byte FunctionNumberInvalid = 0x01;
    private const byte InputAvailable = 0xFF;
    private const byte NoInputAvailable = 0x00;
    private const byte SentinelByte = (byte)'X';

    private static readonly IReadOnlyList<ushort> NoKeys = [];

    /// <summary>Every console and keyboard program scenario, in the order of the service it exercises:
    /// the console and IOCTL scenarios first, then the enhanced INT 16h keyboard scenarios.</summary>
    public static IReadOnlyList<AsmScenario> Scenarios => [.. ConsoleScenarios, .. Int16Scenarios];

    /// <summary>The console and IOCTL scenarios: the INT 21h console character, string and status
    /// services, the IOCTL subfunctions and the console input and break-check services.</summary>
    public static IReadOnlyList<AsmScenario> ConsoleScenarios => [
        Ioctl06KeyboardBufferState("console_ioctl06_keyboard_available", InputAvailable, 0x1579),
        Ioctl06KeyboardBufferState("console_ioctl06_keyboard_empty", NoInputAvailable, 0x0000),
        Ioctl06PollAndReadPreloadedKey(),
        ConsoleReadThroughInt16BlockingPath("console_ah01_read_key", 0x01, 0x2C5A, 'Z'),
        ConsoleReadThroughInt16BlockingPath("console_ah07_read_key", 0x07, 0x254B, 'K'),
        ConsoleReadThroughInt16BlockingPath("console_ah08_read_key", 0x08, 0x2044, 'D'),
        Ioctl00ReportsCharacterDeviceBit(),
        Ioctl06ReportsDataForRedirectedFile(),
        Ioctl06ReportsNoDataForEmptyRedirectedFile(),
        Ioctl06ReportsNoDataAtEndOfFile(),
        Ioctl07ReportsStdoutReady(),
        Ioctl09FixedDriveRegisters(),
        Ioctl0EFloppyDriveRegisters(),
        Ioctl09ZDriveRegisters(),
        Ioctl0EZDriveRegisters(),
        DirectConsoleIoInput("console_ah06_input_key", 0x1E61, 0x61),
        DirectConsoleIoInput("console_ah06_input_no_key", 0x0000, NoInputAvailable),
        DirectConsoleIoOutputRedirected(),
        DisplayOutputChecksForPendingBreak(),
        BreakCheckAfterCall("console_ah03_ctrl_c_break", 0x03),
        BreakCheckAfterCall("console_ah04_ctrl_c_break", 0x04),
        BreakCheckAfterCall("console_ah05_ctrl_c_break", 0x05),
        PrintStringRedirected(),
        PrintStringChecksForPendingBreak(),
        CheckStdinStatusReportsPendingKey(),
        FlushThenDirectConsoleIoStatus(),
        UnsupportedIoctlFunction("console_ioctl_unsupported_0a", 0x0A),
        UnsupportedIoctlFunction("console_ioctl_unsupported_0f", 0x0F),
        UnsupportedIoctlFunction("console_ioctl_unsupported_10", 0x10),
        UnsupportedIoctlFunction("console_ioctl_unsupported_11", 0x11)
    ];

    /// <summary>The enhanced INT 16h keyboard scenarios.</summary>
    public static IReadOnlyList<AsmScenario> Int16Scenarios => [
        Int16Ah10ReturnsExtendedKeyScanCode(),
        Int16Ah11ReturnsExtendedKeyScanCode(),
        Int16WaitKeyThroughChainedInt9(),
        Int16SecondWaitGetsNextKey()
    ];

    /// <summary>IOCTL 06 reports whether a keystroke is waiting in the BIOS keyboard buffer.</summary>
    /// <param name="identity">Stable identity of the buffer state variant.</param>
    /// <param name="expectedStatus">Expected AL result of the IOCTL call.</param>
    /// <param name="keyCode">BIOS key code to preload, or zero for an empty buffer.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl06KeyboardBufferState(string identity, byte expectedStatus, ushort keyCode) {
        return Scenario(identity, run => run
            .ResourceCopy(ProgramDirectory, "ioctl_status_6_0.com", "IOST.COM")
            .RunWithKeys("IOST.COM", Keys(keyCode), result =>
                VideoByte(result, 0).Should().Be(expectedStatus, "IOCTL 06 should reflect keyboard buffer availability")));
    }

    /// <summary>A program polling IOCTL 06 and reading with INT 21h AH=07h once input is available reads
    /// the preloaded key, the pattern CHOICE.COM and similar programs use.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl06PollAndReadPreloadedKey() {
        return Scenario("console_ioctl06_poll_and_read", run => run
            .ResourceCopy(ProgramDirectory, "ioctl_poll_and_read.com", "POLL.COM")
            .RunWithKeys("POLL.COM", [0x1579], result =>
                Cell(result, 0).Should().Be('y',
                    "the poll-and-read pattern should successfully read the pre-loaded keystroke")));
    }

    /// <summary>INT 21h console input with the given AH function reads the preloaded key through the
    /// INT 16h blocking path and returns it.</summary>
    /// <param name="identity">Stable identity of the input function variant.</param>
    /// <param name="ahFunction">INT 21h input function the program calls.</param>
    /// <param name="keyCode">BIOS key code to preload.</param>
    /// <param name="expectedChar">Character the program writes to the first video cell.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario ConsoleReadThroughInt16BlockingPath(string identity, byte ahFunction, ushort keyCode,
        char expectedChar) {
        return Scenario(identity, run => run
            .ResourceCopy(ProgramDirectory, $"console_read_to_video_{ahFunction:x}.com", "READK.COM")
            .RunWithKeys("READK.COM", [keyCode], result =>
                Cell(result, 0).Should().Be(expectedChar,
                    $"AH={ahFunction:X2} should read the key via INT 16h blocking path")));
    }

    /// <summary>IOCTL 00 on stdin reports the character device bit, confirming handle 0 is the CON device.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl00ReportsCharacterDeviceBit() {
        return Scenario("console_ioctl00_stdin_character_device", run => run
            .ResourceCopy(ProgramDirectory, "ioctl_device_info.com", "DEVINFO.COM")
            .RunWithKeys("DEVINFO.COM", NoKeys, result =>
                (VideoByte(result, 0) & CharacterDeviceBit).Should().NotBe(0,
                    "IOCTL 00 on stdin should report character device (bit 7 set in DL)")));
    }

    /// <summary>IOCTL 06 on a redirected file that still has data reports input available.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl06ReportsDataForRedirectedFile() {
        return Scenario("console_ioctl06_redirect_file_data", run => run
            .Text("DATA.TXT", "A")
            .ResourceCopy(ProgramDirectory, "ioctl_status_6_0.com", "IOST.COM")
            .StartBatch("IOST.COM < DATA.TXT\r\n", result =>
                VideoByte(result, 0).Should().Be(InputAvailable,
                    "IOCTL 06 on file with remaining data should report 0xFF (input available)")));
    }

    /// <summary>IOCTL 06 on a redirected file that is already at its end reports no input.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl06ReportsNoDataForEmptyRedirectedFile() {
        return Scenario("console_ioctl06_redirect_file_empty", run => run
            .Text("EMPTY.TXT", "")
            .ResourceCopy(ProgramDirectory, "ioctl_status_6_0.com", "IOST.COM")
            .StartBatch("IOST.COM < EMPTY.TXT\r\n", result =>
                VideoByte(result, 0).Should().Be(NoInputAvailable,
                    "IOCTL 06 on empty file should report 0x00 (no input at EOF)")));
    }

    /// <summary>IOCTL 06 after the program read the last byte of a redirected file reports no input, which
    /// requires the file position to be reported relative to the end of the stream.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl06ReportsNoDataAtEndOfFile() {
        return Scenario("console_ioctl06_redirect_file_eof_after_read", run => run
            .Text("DATA.TXT", "X")
            .ResourceCopy(ProgramDirectory, "read_then_ioctl_input_status.com", "READCHK.COM")
            .StartBatch("READCHK.COM < DATA.TXT\r\n", result =>
                VideoByte(result, 0).Should().Be(NoInputAvailable,
                    "IOCTL 06 after reading all file data should report 0x00 (at EOF)")));
    }

    /// <summary>IOCTL 07 on stdout reports the console device always ready for output.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl07ReportsStdoutReady() {
        return Scenario("console_ioctl07_stdout_ready", run => run
            .ResourceCopy(ProgramDirectory, "ioctl_status_7_1.com", "OUTST.COM")
            .RunWithKeys("OUTST.COM", NoKeys, result =>
                VideoByte(result, 0).Should().Be(InputAvailable,
                    "IOCTL 07 on stdout should report 0xFF (output ready)")));
    }

    /// <summary>IOCTL 09 on a local fixed drive returns the DOSBox register contract.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl09FixedDriveRegisters() {
        return Scenario("console_ioctl09_fixed_drive", run => run
            .ResourceCopy(ProgramDirectory, "ioctl_drive_register_probe_9_3.com", "DRV09.COM")
            .RunWithKeys("DRV09.COM", NoKeys, result => {
                VideoByte(result, 0).Should().Be(0x00,
                    "IOCTL 09 should return AL=00h for a successful local-drive probe");
                VideoByte(result, 1).Should().Be(0x03,
                    "IOCTL 09 should return AH=03h to match DOSBox's AX=0300h contract");
                VideoByte(result, 2).Should().Be(0x02,
                    "IOCTL 09 should report DX low byte 02h for a local fixed drive");
                VideoByte(result, 3).Should().Be(0x08,
                    "IOCTL 09 should report DX high byte 08h for a local fixed drive");
            }));
    }

    /// <summary>IOCTL 0E on the floppy drive A: returns its logical drive mapping.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl0EFloppyDriveRegisters() {
        return Scenario("console_ioctl0e_floppy_drive", run => run
            .ResourceCopy(ProgramDirectory, "ioctl_drive_register_probe_e_1.com", "DRV0E.COM")
            .RunWithKeys("DRV0E.COM", NoKeys, result => {
                VideoByte(result, 0).Should().Be(0x01,
                    "IOCTL 0E should map floppy drive A: to logical drive 1");
                VideoByte(result, 1).Should().Be(0x07,
                    "IOCTL 0E should always return AH=07h on success, including the floppy branch");
            }));
    }

    /// <summary>IOCTL 09 accepts the mounted Z: memory drive as a local drive instead of rejecting it.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl09ZDriveRegisters() {
        return Scenario("console_ioctl09_z_drive", run => run
            .ResourceCopy(ProgramDirectory, "ioctl_drive_register_probe_9_1a.com", "DRV09Z.COM")
            .RunWithKeys("DRV09Z.COM", NoKeys, result => {
                VideoByte(result, 0).Should().Be(0x00,
                    "IOCTL 09 should succeed for a mounted high-letter drive such as Z:");
                VideoByte(result, 1).Should().Be(0x03,
                    "IOCTL 09 should return AH=03h for mounted Z: just like other local drives");
                VideoByte(result, 2).Should().Be(0x02,
                    "IOCTL 09 should report DX low byte 02h for mounted Z:");
                VideoByte(result, 3).Should().Be(0x08,
                    "IOCTL 09 should report DX high byte 08h for mounted Z:");
            }));
    }

    /// <summary>IOCTL 0E reports the mounted Z: as a non-removable logical drive with a single assignment.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Ioctl0EZDriveRegisters() {
        return Scenario("console_ioctl0e_z_drive", run => run
            .ResourceCopy(ProgramDirectory, "ioctl_drive_register_probe_e_1a.com", "DRV0EZ.COM")
            .RunWithKeys("DRV0EZ.COM", NoKeys, result => {
                VideoByte(result, 0).Should().Be(0x00,
                    "IOCTL 0E should return AL=00h for mounted Z: because it has a single logical assignment");
                VideoByte(result, 1).Should().Be(0x07,
                    "IOCTL 0E should return AH=07h for mounted Z:");
            }));
    }

    /// <summary>INT 21h AH=06h in input mode returns the pending character, or nothing when the buffer is
    /// empty.</summary>
    /// <param name="identity">Stable identity of the buffer state variant.</param>
    /// <param name="keyCode">BIOS key code to preload, or zero for an empty buffer.</param>
    /// <param name="expectedAl">Expected AL result of the call.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirectConsoleIoInput(string identity, ushort keyCode, byte expectedAl) {
        return Scenario(identity, run => run
            .ResourceCopy(ProgramDirectory, "direct_console_io_to_video.com", "DCIO.COM")
            .RunWithKeys("DCIO.COM", Keys(keyCode), result =>
                VideoByte(result, 0).Should().Be(expectedAl,
                    "AH=06h with DL=FFh should return the expected AL value in input mode")));
    }

    /// <summary>INT 21h AH=06h in output mode writes the character to the redirected standard output.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirectConsoleIoOutputRedirected() {
        return Scenario("console_ah06_output_redirect", run => run
            .ResourceCopy(ProgramDirectory, "direct_console_io.com", "DCOUT.COM")
            .StartBatch("DCOUT.COM > OUTPUT.TXT\r\n",
                _ => run.TextFile("OUTPUT.TXT").Should().Contain("Q",
                    "AH=06h output mode should write the character to stdout")));
    }

    /// <summary>INT 21h AH=02h checks the standard input for a pending break before writing, so the
    /// sentinel the program writes afterwards never appears.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DisplayOutputChecksForPendingBreak() {
        return Scenario("console_ah02_ctrl_c_break", run => run
            .ResourceCopy(ProgramDirectory, "display_output_then_write_sentinel.com", "AH02C.COM")
            .RunWithKeys("AH02C.COM", [CtrlCBiosKeyCode], result =>
                VideoByte(result, 0).Should().NotBe(SentinelByte,
                    "AH=02h should perform break check and terminate before subsequent instructions")));
    }

    /// <summary>INT 21h with the given AH function checks the standard input for a pending break before
    /// returning, so the sentinel the program writes afterwards never appears.</summary>
    /// <param name="identity">Stable identity of the input function variant.</param>
    /// <param name="ahFunction">INT 21h function the program calls.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario BreakCheckAfterCall(string identity, byte ahFunction) {
        return Scenario(identity, run => run
            .ResourceCopy(ProgramDirectory, $"int21_call_then_write_sentinel_{ahFunction:x}.com", "BRKCHK.COM")
            .RunWithKeys("BRKCHK.COM", [CtrlCBiosKeyCode], result =>
                VideoByte(result, 0).Should().NotBe(SentinelByte,
                    $"AH={ahFunction:X2} should perform break check and terminate before subsequent instructions")));
    }

    /// <summary>INT 21h AH=09h writes its dollar terminated string to the redirected standard output.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario PrintStringRedirected() {
        return Scenario("console_ah09_print_string", run => run
            .ResourceCopy(ProgramDirectory, "print_dollar_string.com", "PRINT.COM")
            .StartBatch("PRINT.COM > OUTPUT.TXT\r\n",
                _ => run.TextFile("OUTPUT.TXT").Should().Contain("Hello DOS",
                    "AH=09h should write the string to stdout (stopping at $)")));
    }

    /// <summary>INT 21h AH=09h checks the standard input for a pending break before writing, so the
    /// sentinel the program writes afterwards never appears.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario PrintStringChecksForPendingBreak() {
        return Scenario("console_ah09_ctrl_c_break", run => run
            .ResourceCopy(ProgramDirectory, "print_string_then_write_sentinel.com", "AH09C.COM")
            .RunWithKeys("AH09C.COM", [CtrlCBiosKeyCode], result =>
                VideoByte(result, 0).Should().NotBe(SentinelByte,
                    "AH=09h should perform break check and terminate before subsequent instructions")));
    }

    /// <summary>INT 21h AH=0Bh reports a pending keystroke in the BIOS keyboard buffer.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CheckStdinStatusReportsPendingKey() {
        return Scenario("console_ah0b_input_available", run => run
            .ResourceCopy(ProgramDirectory, "check_stdin_status.com", "CHKST.COM")
            .RunWithKeys("CHKST.COM", [0x1F73], result =>
                VideoByte(result, 0).Should().Be(InputAvailable,
                    "AH=0Bh should return 0xFF when keyboard buffer has a pending keystroke")));
    }

    /// <summary>INT 21h AH=0Ch with the direct console input subfunction flushes the keyboard buffer
    /// first, so the status the program reads afterwards is empty.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario FlushThenDirectConsoleIoStatus() {
        return Scenario("console_ah0c_flushes_keyboard", run => run
            .ResourceCopy(ProgramDirectory, "ah0c_flush_then_ah06_input_status_to_video.com", "AH0C06.COM")
            .RunWithKeys("AH0C06.COM", [0x1E61], result =>
                VideoByte(result, 0).Should().Be(NoInputAvailable,
                    "AH=0Ch should clear keyboard buffer before invoking subfunction AL=06h")));
    }

    /// <summary>An IOCTL subfunction the emulator does not implement reports the DOS contract for an
    /// invalid function number.</summary>
    /// <param name="identity">Stable identity of the subfunction variant.</param>
    /// <param name="subFunction">IOCTL AL subfunction the program calls.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario UnsupportedIoctlFunction(string identity, byte subFunction) {
        return Scenario(identity, run => run
            .ResourceCopy(ProgramDirectory, $"ioctl_unsupported_function_probe_{subFunction:x}.com", "IOERR.COM")
            .RunWithKeys("IOERR.COM", NoKeys, result => {
                VideoByte(result, 0).Should().Be(CarryFlagReported,
                    $"IOCTL AL={subFunction:X2} should set carry on unsupported function");
                VideoByte(result, 1).Should().Be(FunctionNumberInvalid,
                    $"IOCTL AL={subFunction:X2} should return AX=0001h (FunctionNumberInvalid)");
            }));
    }

    /// <summary>INT 16h AH=10h returns the scan code of an extended key, which the program writes to the
    /// first video cell.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Int16Ah10ReturnsExtendedKeyScanCode() {
        return Scenario("int16_ah10_extended_key_scan_code", run => run
            .ResourceCopy(ProgramDirectory, "read_extended_key_10.com", "READ10.COM")
            .RunWithKeys("READ10.COM", [0x3B00], result =>
                VideoByte(result, 0).Should().Be(0x3B, "AH=10h should return the scan code of F1 in AH")));
    }

    /// <summary>INT 16h AH=11h returns the scan code of the pending extended key without dequeuing any
    /// other data, which the program writes to the first video cell.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Int16Ah11ReturnsExtendedKeyScanCode() {
        return Scenario("int16_ah11_extended_key_scan_code", run => run
            .ResourceCopy(ProgramDirectory, "read_extended_key_11.com", "READ11.COM")
            .RunWithKeys("READ11.COM", [0x3C00], result =>
                VideoByte(result, 0).Should().Be(0x3C, "AH=11h should return the scan code of F2 in AH")));
    }

    /// <summary>A program that hooks INT 9, reads port 60h itself and chains to the BIOS handler still
    /// gets the injected key back from a blocking INT 16h AH=00h, because INT 16h waits with interrupts
    /// enabled and the BIOS INT 9 handler buffers the key the chained handler consumed.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Int16WaitKeyThroughChainedInt9() {
        return Scenario("int16_wait_key_through_chained_int9", run => run
            .ResourceCopy(ProgramDirectory, "int16_wait_key_through_chained_int9.com", "CHAIN9.COM")
            .RunWithKeys("CHAIN9.COM", NoKeys, result => {
                VideoByte(result, 0).Should().Be(0x61, "INT 16h AH=00h should return 'a' in AL");
                VideoByte(result, 1).Should().Be(0x1E, "INT 16h AH=00h should return the scancode of A in AH");
                VideoByte(result, 2).Should().Be(0x1E, "the hooked INT 9 handler should have seen the scancode on port 60h");
            }));
    }

    /// <summary>A second blocking INT 16h AH=00h call waits with interrupts enabled until the next timer
    /// tick injects a 'B' key press, so it returns 'b' instead of the stale previous scancode.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario Int16SecondWaitGetsNextKey() {
        return Scenario("int16_second_wait_gets_next_key", run => run
            .ResourceCopy(ProgramDirectory, "int16_second_wait_gets_next_key.com", "NEXTKEY.COM")
            .RunWithKeys("NEXTKEY.COM", NoKeys, result => {
                VideoByte(result, 0).Should().Be(0x61, "the first INT 16h AH=00h should return 'a'");
                VideoByte(result, 1).Should().Be(0x62, "the second INT 16h AH=00h should return 'b', not the stale 'a' again");
            }));
    }

    private static AsmScenario Scenario(string identity, Func<DosProgramRun, AsmFixture> build) {
        return new AsmScenario(identity, scope => build(new DosProgramRun(scope, identity)), []);
    }

    private static IReadOnlyList<ushort> Keys(ushort keyCode) {
        return keyCode == 0 ? NoKeys : [keyCode];
    }
}
