namespace Spice86.Tests.Dos;

using FluentAssertions;

using System;
using System.IO;

using Xunit;

using static BatchTestHelpers;

public class DosConsoleIoctlIntegrationTests {
    private static void AssertConsoleReadFromPreloadedKey(byte ahFunction, ushort keyCode, char expectedChar,
        string tempDirectoryName) {
        WithTempFile(tempDirectoryName, tempDir => {
            // Arrange
            string comPath = CreateBinaryFile(tempDir, "READK.COM", BuildConsoleReadToVideoCom(ahFunction, 0));
            ushort[] keys = new ushort[] { keyCode };

            // Act
            char[] video = RunWithPreloadedKeysAndCaptureVideoCells(comPath, tempDir, 1, keys);

            // Assert
            video[0].Should().Be(expectedChar,
                $"AH={ahFunction:X2} should read the key via INT 16h blocking path");
        });
    }

    private static ushort[] ToPreloadedKeys(ushort keyCode) {
        return keyCode == 0 ? Array.Empty<ushort>() : new ushort[] { keyCode };
    }

    private static void AssertFirstVideoByteFromRedirectedScript(string tempDir, string comName, byte[] comProgram,
        string redirectedScript, byte expectedValue, string because) {
        // Arrange
        CreateBinaryFile(tempDir, comName, comProgram);

        // Act + Assert
        AssertFirstVideoByteFromScript(tempDir, redirectedScript, expectedValue, because);
    }

    /// <summary>
    /// IOCTL 06 (Get Input Status) on stdin should report 0xFF when a key is pre-loaded
    /// in the BIOS keyboard buffer.
    /// </summary>
    [Theory]
    [InlineData("dos_ioctl06_available", 0xFF, 0x1579)]
    [InlineData("dos_ioctl06_empty", 0x00, 0x0000)]
    public void Ioctl06_ReportsExpectedInputStatus_ForKeyboardBufferState(string tempDirectoryName,
        byte expectedStatus, ushort keyCode) {
        WithTempFile(tempDirectoryName, tempDir => {
            // Arrange
            string comPath = CreateBinaryFile(tempDir, "IOST.COM", BuildIoctlInputStatusCom(0));
            ushort[] keys = ToPreloadedKeys(keyCode);

            // Act + Assert
            AssertFirstVideoByteWithPreloadedKeys(comPath, tempDir, keys, expectedStatus,
                "IOCTL 06 should reflect keyboard buffer availability");
        });
    }

    /// <summary>
    /// A COM program that polls IOCTL 06 and reads a character when available
    /// should successfully read the pre-loaded key. This demonstrates the real-world
    /// pattern used by CHOICE.COM and similar programs.
    /// </summary>
    [Fact]
    public void Ioctl06PollAndRead_ReadsPreloadedKey() {
        WithTempFile("dos_ioctl06_poll_read", tempDir => {
            // Arrange: COM that polls IOCTL 06 then reads via AH=07h
            string comPath = CreateBinaryFile(tempDir, "POLL.COM", BuildIoctlPollAndReadCom(0));

            // Pre-load 'y' key (scan=0x15, ascii=0x79)
            ushort[] keys = new ushort[] { 0x1579 };

            // Act
            char[] video = RunWithPreloadedKeysAndCaptureVideoCells(comPath, tempDir, 1, keys);

            // Assert: should have read 'y'
            video[0].Should().Be('y',
                "the poll-and-read pattern should successfully read the pre-loaded keystroke");
        });
    }

    [Theory]
    [InlineData(0x01, 0x2C5A, 'Z', "dos_ah01_echo")]
    [InlineData(0x07, 0x254B, 'K', "dos_ah07_blocking")]
    [InlineData(0x08, 0x2044, 'D', "dos_ah08_blocking")]
    public void Int21hAh01Ah07Ah08_ReadPreloadedKey_ViaInt16hBlockingPath(byte ahFunction,
        ushort keyCode, char expectedChar, string tempDirectoryName) {
        AssertConsoleReadFromPreloadedKey(ahFunction, keyCode, expectedChar, tempDirectoryName);
    }

    /// <summary>
    /// IOCTL 00 (Get Device Information) on stdin should return device info with the character device
    /// bit (0x80) set in DL, confirming that handle 0 points to the CON character device.
    /// </summary>
    [Fact]
    public void Ioctl00_ReturnsCharacterDeviceBit_ForConsoleStdin() {
        WithTempFile("dos_ioctl00_stdin", tempDir => {
            // Arrange: COM that calls IOCTL 00 on handle 0 (stdin) and writes DL to video
            string comPath = CreateBinaryFile(tempDir, "DEVINFO.COM", BuildIoctlDeviceInfoCom(0, 0));

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 1, Array.Empty<ushort>());

            // Assert: DL should have bit 7 (0x80) set — character device flag
            (video[0] & 0x80).Should().NotBe(0,
                "IOCTL 00 on stdin should report character device (bit 7 set in DL)");
        });
    }

    /// <summary>
    /// IOCTL 06 on stdin redirected from a file with data should report input available (0xFF)
    /// because the file position is before EOF.
    /// </summary>
    [Fact]
    public void Ioctl06_ReportsDataAvailable_ForRedirectedFileWithData() {
        WithTempFile("dos_ioctl06_file_data", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "DATA.TXT", "A");

            // Act + Assert
            AssertFirstVideoByteFromRedirectedScript(tempDir, "IOST.COM", BuildIoctlInputStatusCom(0),
                "IOST.COM < DATA.TXT\r\n", 0xFF,
                "IOCTL 06 on file with remaining data should report 0xFF (input available)");
        });
    }

    /// <summary>
    /// IOCTL 06 on stdin redirected from an empty file should report no input (0x00)
    /// because the file is at EOF from the start.
    /// </summary>
    [Fact]
    public void Ioctl06_ReportsNoData_ForRedirectedEmptyFile() {
        WithTempFile("dos_ioctl06_file_empty", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "EMPTY.TXT", "");

            // Act + Assert
            AssertFirstVideoByteFromRedirectedScript(tempDir, "IOST.COM", BuildIoctlInputStatusCom(0),
                "IOST.COM < EMPTY.TXT\r\n", 0x00,
                "IOCTL 06 on empty file should report 0x00 (no input at EOF)");
        });
    }

    /// <summary>
    /// IOCTL 06 on stdin redirected from a 1-byte file after reading that byte should report
    /// no input (0x00) because the file position is at EOF. This catches the seek offset bug
    /// where Seek(Position, SeekOrigin.End) was used instead of Seek(0, SeekOrigin.End).
    /// </summary>
    [Fact]
    public void Ioctl06_ReportsNoData_ForRedirectedFileAtEofAfterRead() {
        WithTempFile("dos_ioctl06_file_eof", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "DATA.TXT", "X");

            // Act + Assert
            AssertFirstVideoByteFromRedirectedScript(tempDir, "READCHK.COM", BuildReadThenIoctlInputStatusCom(0),
                "READCHK.COM < DATA.TXT\r\n", 0x00,
                "IOCTL 06 after reading all file data should report 0x00 (at EOF)");
        });
    }

    /// <summary>
    /// IOCTL 07 (Get Output Status) on stdout should report ready (0xFF) because
    /// the console device is always ready for output.
    /// </summary>
    [Fact]
    public void Ioctl07_ReportsOutputReady_ForStdout() {
        WithTempFile("dos_ioctl07_stdout", tempDir => {
            // Arrange
            string comPath = CreateBinaryFile(tempDir, "OUTST.COM", BuildIoctlOutputStatusCom(1, 0));

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 1, Array.Empty<ushort>());

            // Assert
            video[0].Should().Be(0xFF,
                "IOCTL 07 on stdout should report 0xFF (output ready)");
        });
    }

    [Fact]
    public void Ioctl09_ReturnsDosBoxRegisterContract_ForLocalFixedDrive() {
        WithTempFile("dos_ioctl09_fixed_drive", tempDir => {
            // Arrange: BX=3 selects C: (1-based DOS drive numbering).
            string comPath = CreateBinaryFile(tempDir, "DRV09.COM",
                BuildIoctlDriveRegisterProbeCom(0x09, 3, 0));

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 4, Array.Empty<ushort>());

            // Assert: DOSBox returns AX=0300h and DX=0802h for a local fixed drive.
            video[0].Should().Be(0x00,
                "IOCTL 09 should return AL=00h for a successful local-drive probe");
            video[1].Should().Be(0x03,
                "IOCTL 09 should return AH=03h to match DOSBox's AX=0300h contract");
            video[2].Should().Be(0x02,
                "IOCTL 09 should report DX low byte 02h for a local fixed drive");
            video[3].Should().Be(0x08,
                "IOCTL 09 should report DX high byte 08h for a local fixed drive");
        });
    }

    [Fact]
    public void Ioctl0E_ReturnsAh07_ForFloppyDrive() {
        WithTempFile("dos_ioctl0e_floppy_drive", tempDir => {
            // Arrange: BX=1 selects drive A:.
            string comPath = CreateBinaryFile(tempDir, "DRV0E.COM",
                BuildIoctlDriveRegisterProbeCom(0x0E, 1, 0));

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 4, Array.Empty<ushort>());

            // Assert: DOSBox returns AL=01h and AH=07h for drive A:.
            video[0].Should().Be(0x01,
                "IOCTL 0E should map floppy drive A: to logical drive 1");
            video[1].Should().Be(0x07,
                "IOCTL 0E should always return AH=07h on success, including the floppy branch");
        });
    }

    [Fact]
    public void Ioctl09_ReturnsDosBoxRegisterContract_ForMountedMemoryDriveZ() {
        WithTempFile("dos_ioctl09_z_drive", tempDir => {
            // Arrange: BX=26 selects Z: (1-based DOS drive numbering).
            string comPath = CreateBinaryFile(tempDir, "DRV09Z.COM",
                BuildIoctlDriveRegisterProbeCom(0x09, 26, 0));

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 4, Array.Empty<ushort>());

            // Assert: mounted Z: must be accepted as a valid local drive, not rejected by count-based validation.
            video[0].Should().Be(0x00,
                "IOCTL 09 should succeed for a mounted high-letter drive such as Z:");
            video[1].Should().Be(0x03,
                "IOCTL 09 should return AH=03h for mounted Z: just like other local drives");
            video[2].Should().Be(0x02,
                "IOCTL 09 should report DX low byte 02h for mounted Z:");
            video[3].Should().Be(0x08,
                "IOCTL 09 should report DX high byte 08h for mounted Z:");
        });
    }

    [Fact]
    public void Ioctl0E_ReturnsLogicalDriveMap_ForMountedMemoryDriveZ() {
        WithTempFile("dos_ioctl0e_z_drive", tempDir => {
            // Arrange: BX=26 selects Z: (1-based DOS drive numbering).
            string comPath = CreateBinaryFile(tempDir, "DRV0EZ.COM",
                BuildIoctlDriveRegisterProbeCom(0x0E, 26, 0));

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 4, Array.Empty<ushort>());

            // Assert: mounted Z: is a valid non-removable logical drive with only one assignment.
            video[0].Should().Be(0x00,
                "IOCTL 0E should return AL=00h for mounted Z: because it has a single logical assignment");
            video[1].Should().Be(0x07,
                "IOCTL 0E should return AH=07h for mounted Z:");
        });
    }

    [Theory]
    [InlineData("dos_ah06_input_key", 0x1E61, 0x61)]
    [InlineData("dos_ah06_input_nokey", 0x0000, 0x00)]
    public void Int21hAh06_InputMode_ReturnsExpectedValue(string tempDirectoryName, ushort keyCode,
        byte expectedAl) {
        WithTempFile(tempDirectoryName, tempDir => {
            // Arrange
            string comPath = CreateBinaryFile(tempDir, "DCIO.COM", BuildDirectConsoleIoInputCom(0));
            ushort[] keys = ToPreloadedKeys(keyCode);

            // Act + Assert
            AssertFirstVideoByteWithPreloadedKeys(comPath, tempDir, keys, expectedAl,
                "AH=06h with DL=FFh should return the expected AL value in input mode");
        });
    }

    /// <summary>
    /// INT 21h AH=06h with DL != 0xFF (output mode) should write the character to stdout.
    /// Verified by redirecting stdout to a file and reading it back.
    /// </summary>
    [Fact]
    public void Int21hAh06_OutputMode_WritesCharToRedirectedStdout() {
        WithTempFile("dos_ah06_output", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "DCOUT.COM", BuildDirectConsoleIoOutputCom((byte)'Q'));

            // Act: redirect stdout to a file
            RunBatchScript(tempDir, "DCOUT.COM > OUTPUT.TXT\r\n");

            // Assert: the output file should contain 'Q'
            string output = File.ReadAllText(Path.Join(tempDir, "OUTPUT.TXT"));
            output.Should().Contain("Q",
                "AH=06h output mode should write the character to stdout");
        });
    }

    /// <summary>
    /// INT 21h AH=02h (Display Output) checks STDIN for Ctrl-C/Ctrl-Break before writing.
    /// FreeDOS performs this check via check_handle_break before dispatching AH=02h.
    ///
    /// With Ctrl-C preloaded in the keyboard buffer, AH=02h should trigger INT 23h behavior
    /// and terminate, so the sentinel write after AH=02h must not execute.
    /// </summary>
    [Fact]
    public void Int21hAh02_DoesNotContinue_WhenCtrlCIsPendingInStdin() {
        WithTempFile("dos_ah02_ctrlc", tempDir => {
            // Arrange: program calls AH=02h then writes sentinel 'X' directly to video cell 0.
            // If break handling is correct, execution terminates before sentinel write.
            string comPath = CreateBinaryFile(tempDir, "AH02C.COM",
                BuildDisplayOutputThenWriteSentinelCom((byte)'Q', (byte)'X'));
            ushort[] keys = new ushort[] { 0x0003 }; // pending Ctrl-C in BIOS keyboard buffer

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 1, keys);

            // Assert: sentinel must not be written when Ctrl-C interrupts AH=02h path.
            video[0].Should().NotBe((byte)'X',
                "AH=02h should perform break check and terminate before subsequent instructions");
        });
    }

    [Theory]
    [InlineData(0x03)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    public void Int21hAh03Ah04Ah05_DoesNotContinue_WhenCtrlCIsPendingInStdin(byte functionAh) {
        WithTempFile($"dos_ah{functionAh:X2}_ctrlc", tempDir => {
            // Arrange: if break path executes, sentinel write is never reached.
            string comPath = CreateBinaryFile(tempDir, "BRKCHK.COM",
                BuildInt21CallThenWriteSentinelCom(functionAh, (byte)'X'));
            ushort[] keys = new ushort[] { 0x0003 };

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 1, keys);

            // Assert
            video[0].Should().NotBe((byte)'X',
                $"AH={functionAh:X2} should perform break check and terminate before subsequent instructions");
        });
    }

    /// <summary>
    /// INT 21h AH=09h (Print String) should write a $-terminated string to stdout.
    /// Verified by redirecting stdout to a file and reading it back.
    /// </summary>
    [Fact]
    public void Int21hAh09_PrintsStringToRedirectedStdout() {
        WithTempFile("dos_ah09_string", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "PRINT.COM", BuildPrintDollarStringCom("Hello DOS"));

            // Act
            RunBatchScript(tempDir, "PRINT.COM > OUTPUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUTPUT.TXT"));
            output.Should().Contain("Hello DOS",
                "AH=09h should write the string to stdout (stopping at $)");
        });
    }

    [Fact]
    public void Int21hAh09_DoesNotContinue_WhenCtrlCIsPendingInStdin() {
        WithTempFile("dos_ah09_ctrlc", tempDir => {
            // Arrange
            string comPath = CreateBinaryFile(tempDir, "AH09C.COM",
                BuildPrintStringThenWriteSentinelCom("HELLO", (byte)'X'));
            ushort[] keys = new ushort[] { 0x0003 };

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 1, keys);

            // Assert
            video[0].Should().NotBe((byte)'X',
                "AH=09h should perform break check and terminate before subsequent instructions");
        });
    }

    /// <summary>
    /// INT 21h AH=0Bh (Check Standard Input Status) should return AL=0xFF when a key is
    /// pre-loaded in the BIOS keyboard buffer.
    /// </summary>
    [Fact]
    public void Int21hAh0B_ReportsInputAvailable_WhenKeyPreloaded() {
        WithTempFile("dos_ah0b_available", tempDir => {
            // Arrange
            string comPath = CreateBinaryFile(tempDir, "CHKST.COM", BuildCheckStdinStatusCom(0));
            ushort[] keys = new ushort[] { 0x1F73 }; // 's' key (scan=0x1F, ascii=0x73)

            // Act + Assert
            AssertFirstVideoByteWithPreloadedKeys(comPath, tempDir, keys, 0xFF,
                "AH=0Bh should return 0xFF when keyboard buffer has a pending keystroke");
        });
    }

    [Fact]
    public void Int21hAh0C_FlushesKeyboardBuffer_BeforeInvokingAh06() {
        WithTempFile("dos_ah0c_flush", tempDir => {
            // Arrange: AH=0Ch with AL=06h should flush pending keyboard input first.
            string comPath = CreateBinaryFile(tempDir, "AH0C06.COM",
                BuildAh0CFlushThenAh06InputStatusToVideoCom(0));
            ushort[] keys = new ushort[] { 0x1E61 }; // pending 'a'

            // Act + Assert
            AssertFirstVideoByteWithPreloadedKeys(comPath, tempDir, keys, 0x00,
                "AH=0Ch should clear keyboard buffer before invoking subfunction AL=06h");
        });
    }

    [Theory]
    [InlineData(0x0A)]
    [InlineData(0x0F)]
    [InlineData(0x10)]
    [InlineData(0x11)]
    public void Ioctl_OutOfScopeSubfunctions_ReturnConsistentFunctionInvalidError(byte subFunction) {
        WithTempFile($"dos_ioctl_unsupported_{subFunction:X2}", tempDir => {
            // Arrange
            string comPath = CreateBinaryFile(tempDir, "IOERR.COM",
                BuildIoctlUnsupportedFunctionProbeCom(subFunction));

            // Act
            byte[] video = RunWithPreloadedKeysAndCaptureVideoBytes(comPath, tempDir, 2, Array.Empty<ushort>());

            // Assert: DOS error contract for unsupported function is CF=1, AX=0001h.
            video[0].Should().Be(0x01,
                $"IOCTL AL={subFunction:X2} should set carry on unsupported function");
            video[1].Should().Be(0x01,
                $"IOCTL AL={subFunction:X2} should return AX=0001h (FunctionNumberInvalid)");
        });
    }
}
