namespace Spice86.Tests.Dos;

using FluentAssertions;

using System.IO;

using Xunit;

using static BatchTestHelpers;

public class DosBatchRoutingIntegrationTests {
    [Fact]
    public void HostRequestedCom_StillExecutesThroughBatchRoutingPipeline() {
        WithTempDirectory("dos_host_com", tempDir => {
            // Arrange
            string writerComPath = CreateBinaryFile(tempDir, "WRITER.COM", BuildVideoWriterCom('C', 0));

            // Act
            char actual = RunAndCaptureVideoCell(writerComPath, tempDir);

            // Assert
            actual.Should().Be('C');
        });
    }

    [Fact]
    public void HostRequestedBatch_UsesInternalCallAndLaunchesComProgram() {
        WithTempDirectory("dos_batch_call", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITER.COM", BuildVideoWriterCom('A', 0));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT", "CALL WRITER.COM\r\n");

            // Act
            char actual = RunAndCaptureVideoCell(startBatchPath, tempDir);

            // Assert
            actual.Should().Be('A');
        });
    }

    [Fact]
    public void HostRequestedBatch_CallWithoutExtension_PrefersComOverBat() {
        WithTempDirectory("dos_batch_call_noext_bat_first", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "TOOL.BAT", "CALL BATWIN.COM\r\n");
            CreateBinaryFile(tempDir, "TOOL.COM", BuildVideoWriterCom('C', 0));
            CreateBinaryFile(tempDir, "BATWIN.COM", BuildVideoWriterCom('B', 0));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT", "CALL TOOL\r\n");

            // Act
            char actual = RunAndCaptureVideoCell(startBatchPath, tempDir);

            // Assert
            actual.Should().Be('C');
        });
    }

    [Fact]
    public void HostRequestedBatch_CallWithoutExtension_FallsBackToComThenExe() {
        WithTempDirectory("dos_batch_call_noext_com_then_exe", tempDir => {
            // Arrange
            string toolComPath = CreateBinaryFile(tempDir, "TOOL.COM", BuildVideoWriterCom('C', 0));
            CreateBinaryFile(tempDir, "TOOL.EXE", BuildVideoWriterCom('E', 0));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT", "CALL TOOL\r\n");

            // Act
            char firstRun = RunAndCaptureVideoCell(startBatchPath, tempDir);
            File.Delete(toolComPath);
            char secondRun = RunAndCaptureVideoCell(startBatchPath, tempDir);

            // Assert
            firstRun.Should().Be('C');
            secondRun.Should().Be('E');
        });
    }

    [Fact]
    public void HostRequestedBatch_MaupitiTatouBat_RunsFromNestedJeuxDirectory() {
        WithTempDirectory("dos_batch_maupiti_tatou", tempDir => {
            // Arrange
            string maupitiDirectoryPath = CreateDirectoryPath(tempDir, "JEUX", "MAUPITI");
            string tatouBatchPath = CreateTextFile(maupitiDirectoryPath, "TATOU.BAT",
                "CALL MAUPITIW.COM > C:\\JEUX\\MAUPITI\\MAUPITI.TXT\r\n");
            CreateBinaryFile(maupitiDirectoryPath, "MAUPITIW.COM", BuildStdoutWriterCom("M"));
            string outputPath = Path.Join(maupitiDirectoryPath, "MAUPITI.TXT");

            // Act
            RunWithoutVideoRead(tatouBatchPath, tempDir, enablePit: false);
            string output = File.ReadAllText(outputPath);

            // Assert
            output.Should().Be("M");
        });
    }

    [Fact]
    public void HostRequestedBatch_AitdTatouWithoutExtension_PrefersComOverBatAndExe() {
        WithTempDirectory("dos_batch_aitd_tatou_noext", tempDir => {
            // Arrange
            string aitdDirectoryPath = CreateDirectoryPath(tempDir, "JEUX", "AITD");
            CreateTextFile(aitdDirectoryPath, "TATOU.BAT", "ECHO BAT> C:\\JEUX\\AITD\\WINNER.TXT\r\n");
            CreateBinaryFile(aitdDirectoryPath, "TATOU.COM", BuildVideoWriterCom('C', 0));
            CreateBinaryFile(aitdDirectoryPath, "TATOU.EXE", BuildVideoWriterCom('E', 0));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT", "CALL c:\\jeux\\aitd\\tatou\r\n");
            string outputPath = Path.Join(aitdDirectoryPath, "WINNER.TXT");
            string rootOutputPath = Path.Join(tempDir, "WINNER.TXT");

            // Act
            char actual = RunAndCaptureVideoCell(startBatchPath, tempDir);

            // Assert
            actual.Should().Be('C');
            File.Exists(outputPath).Should().BeFalse();
            File.Exists(rootOutputPath).Should().BeFalse();
        });
    }

    [Fact]
    public void HostRequestedBatch_CanCallNestedBatch() {
        WithTempDirectory("dos_nested_batch", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITER.COM", BuildVideoWriterCom('B', 0));
            CreateTextFile(tempDir, "CHILD.BAT", "CALL WRITER.COM\r\n");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT", "CALL CHILD.BAT\r\n");

            // Act
            char actual = RunAndCaptureVideoCell(startBatchPath, tempDir);

            // Assert
            actual.Should().Be('B');
        });
    }

    [Fact]
    public void HostRequestedBatch_GotoSkipsCommands() {
        WithTempDirectory("dos_batch_goto", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "SKIP.COM", BuildVideoWriterCom('S', 2));
            CreateBinaryFile(tempDir, "PASS.COM", BuildVideoWriterCom('G', 0));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "GOTO RUN\r\nCALL SKIP.COM\r\n:RUN\r\nCALL PASS.COM\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('G');
            cells[1].Should().NotBe('S');
        });
    }

    [Fact]
    public void HostRequestedBatch_IfErrorLevelDispatchesCommand() {
        WithTempDirectory("dos_batch_if_errorlevel", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "RET5.COM", BuildExitCodeCom(5));
            CreateBinaryFile(tempDir, "W5.COM", BuildVideoWriterCom('E', 0));

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CALL RET5.COM\r\nIF ERRORLEVEL 5 CALL W5.COM\r\n", 'E');
        });
    }

    [Fact]
    public void HostRequestedBatch_IfErrorLevelWithEqualsSyntaxDispatchesCommand() {
        WithTempDirectory("dos_batch_if_errorlevel_equals", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "RET10.COM", BuildExitCodeCom(10));
            CreateBinaryFile(tempDir, "W10.COM", BuildVideoWriterCom('X', 0));

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CALL RET10.COM\r\nIF ERRORLEVEL = 10 CALL W10.COM\r\n", 'X');
        });
    }

    [Theory]
    [InlineData(0x3B00, 'F')]
    [InlineData(0x3C00, 'E')]
    [InlineData(0x3D00, 'D')]
    public void HostRequestedBatch_AlphaWavesTestfkeyStyleRoutesF1ToF3(ushort keyCode, char expectedMainAction) {
        WithTempDirectory("dos_batch_alpha_waves_testfkey", tempDir => {
            // Arrange: TESTFKEY.COM is the real fixture used by Alpha Waves style scripts.
            CreateBinaryFile(tempDir, "TESTFKEY.COM", LoadDosBatchResourceBinary("TESTFKEY.COM"));

            CreateBinaryFile(tempDir, "ALPHA_F.COM", BuildVideoWriterCom('F', 0));
            CreateBinaryFile(tempDir, "ALPHA_E.COM", BuildVideoWriterCom('E', 0));
            CreateBinaryFile(tempDir, "ALPHA_D.COM", BuildVideoWriterCom('D', 0));
            CreateBinaryFile(tempDir, "ALPHA_X.COM", BuildVideoWriterCom('X', 6));

            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "@echo off\r\n" +
                ":START\r\n" +
                "testfkey\r\n" +
                "if ERRORLEVEL = 10 goto FIN\r\n" +
                "if ERRORLEVEL = 4 goto START\r\n" +
                "if ERRORLEVEL = 3 goto F3\r\n" +
                "if ERRORLEVEL = 2 goto F2\r\n" +
                "if ERRORLEVEL = 1 goto F1\r\n" +
                "goto START\r\n" +
                ":F3\r\n" +
                "ALPHA_D\r\n" +
                "goto FIN\r\n" +
                ":F2\r\n" +
                "ALPHA_E\r\n" +
                "goto FIN\r\n" +
                ":F1\r\n" +
                "ALPHA_F\r\n" +
                "goto FIN\r\n" +
                ":FIN\r\n" +
                "ALPHA_X\r\n" +
                "exit\r\n");

            ushort[] keys = new ushort[] { keyCode };

            // Act
            char[] cells = RunWithPreloadedKeysAndCaptureVideoCells(startBatchPath, tempDir, 4, keys);

            // Assert: F1/F2/F3 dispatch branch payload and then reach FIN.
            cells[0].Should().Be(expectedMainAction);
            cells[3].Should().Be('X');
        });
    }

    [Fact]
    public void HostRequestedBatch_AlphaWavesTestfkeyStyleF10JumpsToFinWithoutF1F2F3Branch() {
        WithTempDirectory("dos_batch_alpha_waves_testfkey_f10", tempDir => {
            // Arrange: TESTFKEY.COM is the real fixture used by Alpha Waves style scripts.
            CreateBinaryFile(tempDir, "TESTFKEY.COM", LoadDosBatchResourceBinary("TESTFKEY.COM"));

            CreateBinaryFile(tempDir, "ALPHA_F.COM", BuildVideoWriterCom('F', 0));
            CreateBinaryFile(tempDir, "ALPHA_E.COM", BuildVideoWriterCom('E', 0));
            CreateBinaryFile(tempDir, "ALPHA_D.COM", BuildVideoWriterCom('D', 0));
            CreateBinaryFile(tempDir, "ALPHA_X.COM", BuildVideoWriterCom('X', 6));

            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "@echo off\r\n" +
                ":START\r\n" +
                "testfkey\r\n" +
                "if ERRORLEVEL = 10 goto FIN\r\n" +
                "if ERRORLEVEL = 4 goto START\r\n" +
                "if ERRORLEVEL = 3 goto F3\r\n" +
                "if ERRORLEVEL = 2 goto F2\r\n" +
                "if ERRORLEVEL = 1 goto F1\r\n" +
                "goto START\r\n" +
                ":F3\r\n" +
                "ALPHA_D\r\n" +
                "goto FIN\r\n" +
                ":F2\r\n" +
                "ALPHA_E\r\n" +
                "goto FIN\r\n" +
                ":F1\r\n" +
                "ALPHA_F\r\n" +
                "goto FIN\r\n" +
                ":FIN\r\n" +
                "ALPHA_X\r\n" +
                "exit\r\n");

            ushort[] keys = new ushort[] { 0x4400 };

            // Act
            char[] cells = RunWithPreloadedKeysAndCaptureVideoCells(startBatchPath, tempDir, 4, keys);

            // Assert: F10 should bypass F1/F2/F3 payloads and jump directly to FIN.
            cells[0].Should().NotBe('F');
            cells[0].Should().NotBe('E');
            cells[0].Should().NotBe('D');
            cells[3].Should().Be('X');
        });
    }

    private static byte[] LoadDosBatchResourceBinary(string fileName) {
        string path = Path.Join(AppContext.BaseDirectory, "Resources", "DosBatchTests", fileName);
        File.Exists(path).Should().BeTrue($"resource fixture should exist: {path}");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void HostRequestedBatch_EchoPreservesLeadingSpacesInOutput() {
        WithTempDirectory("dos_batch_echo_leading_spaces", tempDir => {
            // Arrange
            string outPath = Path.Join(tempDir, "OUT.TXT");

            // Act
            RunBatchScript(tempDir,
                "ECHO            ALPHA WAVES > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(outPath);
            output.Should().StartWith("           ALPHA WAVES");
        });
    }

    [Fact]
    public void HostRequestedBatch_IfNotErrorLevelSkipsCommand() {
        WithTempDirectory("dos_batch_if_not_errorlevel", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "RET5.COM", BuildExitCodeCom(5));
            CreateBinaryFile(tempDir, "SKIP.COM", BuildVideoWriterCom('S', 0));
            CreateBinaryFile(tempDir, "PASS.COM", BuildVideoWriterCom('P', 2));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CALL RET5.COM\r\nIF NOT ERRORLEVEL 5 CALL SKIP.COM\r\nIF ERRORLEVEL 5 CALL PASS.COM\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().NotBe('S');
            cells[1].Should().Be('P');
        });
    }

    [Fact]
    public void HostRequestedBatch_IfNotStringComparisonExecutesExpectedBranch() {
        WithTempDirectory("dos_batch_if_not_string", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "SKIP.COM", BuildVideoWriterCom('S', 0));
            CreateBinaryFile(tempDir, "PASS.COM", BuildVideoWriterCom('N', 2));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "IF NOT \"ONE\"==\"ONE\" CALL SKIP.COM\r\nIF NOT \"ONE\"==\"TWO\" CALL PASS.COM\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().NotBe('S');
            cells[1].Should().Be('N');
        });
    }

    [Fact]
    public void HostRequestedBatch_IfExistAndIfNotExistUseCorrectBranch() {
        WithTempDirectory("dos_batch_if_exist", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "EXISTS.COM", BuildVideoWriterCom('E', 0));
            CreateBinaryFile(tempDir, "MISSING.COM", BuildVideoWriterCom('M', 2));
            CreateTextFile(tempDir, "FLAG.TXT", "1");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "IF EXIST FLAG.TXT CALL EXISTS.COM\r\nIF NOT EXIST FLAG.TXT CALL MISSING.COM\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('E');
            cells[1].Should().NotBe('M');
        });
    }

    [Fact]
    public void HostRequestedBatch_ShiftUpdatesArguments() {
        WithTempDirectory("dos_batch_shift", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('1', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('2', 2));
            CreateTextFile(tempDir, "ROUTE.BAT",
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nSHIFT\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\n");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT", "CALL ROUTE.BAT ONE TWO\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('1');
            cells[1].Should().Be('2');
        });
    }

    [Fact]
    public void HostRequestedBatch_ForIteratesAndCallsBatch() {
        WithTempDirectory("dos_batch_for", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('O', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('T', 2));
            CreateTextFile(tempDir, "ROUTE.BAT",
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\n");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "FOR %%I IN(ONE TWO) DO CALL ROUTE.BAT %%I\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('O');
            cells[1].Should().Be('T');
        });
    }

    [Fact]
    public void HostRequestedBatch_ForSupportsCommaAndSemicolonDelimiters() {
        WithTempDirectory("dos_batch_for_delimiters", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            CreateBinaryFile(tempDir, "W3.COM", BuildVideoWriterCom('C', 4));
            CreateTextFile(tempDir, "ROUTE.BAT",
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\nIF \"%1\"==\"THREE\" CALL W3.COM\r\n");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "FOR %%I IN (ONE,TWO;THREE) DO CALL ROUTE.BAT %%I\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 3);

            // Assert
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
            cells[2].Should().Be('C');
        });
    }

    [Fact]
    public void HostRequestedBatch_ForSupportsEqualsDelimiter() {
        WithTempDirectory("dos_batch_for_equals_delimiter", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            CreateBinaryFile(tempDir, "W3.COM", BuildVideoWriterCom('C', 4));
            CreateTextFile(tempDir, "ROUTE.BAT",
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\nIF \"%1\"==\"THREE\" CALL W3.COM\r\n");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "FOR %%I IN (ONE=TWO=THREE) DO CALL ROUTE.BAT %%I\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 3);

            // Assert
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
            cells[2].Should().Be('C');
        });
    }

    [Fact]
    public void HostRequestedBatch_OutputRedirectionWritesToFile() {
        WithTempDirectory("dos_batch_redirection", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "OUT.COM", BuildStdoutWriterCom("OK"));

            // Act
            RunBatchScript(tempDir, "CALL OUT.COM > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Be("OK");
        });
    }

    [Theory]
    [InlineData("ECHO TEST>OUT.TXT", "TEST\r\n")]
    [InlineData("ECHO TEST >OUT.TXT", "TEST \r\n")]
    [InlineData("ECHO TEST> OUT.TXT", "TEST\r\n")]
    [InlineData("ECHO TEST > OUT.TXT", "TEST \r\n")]
    [InlineData("ECHO TEST>OUT.TXT  ", "TEST  \r\n")]
    [InlineData("ECHO TEST > OUT.TXT ", "TEST  \r\n")]
    public void HostRequestedBatch_OutputRedirectionPreservesEchoSpacing(string batchLine, string expectedOutput) {
        WithTempDirectory("dos_batch_redirection_spacing", tempDir => {
            // Act
            RunBatchScript(tempDir, batchLine + "\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Be(expectedOutput);
        });
    }

    [Fact]
    public void HostRequestedBatch_AppendRedirectionAppendsToFile() {
        WithTempDirectory("dos_batch_append_redirection", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "FIRST.COM", BuildStdoutWriterCom("AA"));
            CreateBinaryFile(tempDir, "SECOND.COM", BuildStdoutWriterCom("BB"));

            // Act
            RunBatchScript(tempDir, "CALL FIRST.COM > OUT.TXT\r\nCALL SECOND.COM >> OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Be("AABB");
        });
    }

    [Theory]
    [InlineData("CALL PROD.COM | CALL CONS.COM")]
    [InlineData("CALL PROD.COM|CALL CONS.COM")]
    [InlineData("CALL PROD.COM| CALL CONS.COM")]
    public void HostRequestedBatch_PipeTransfersStdoutToStdin(string batchLine) {
        WithTempDirectory("dos_batch_pipe", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "PROD.COM", BuildStdoutWriterCom("P"));
            CreateBinaryFile(tempDir, "CONS.COM", BuildStdinToVideoWriterCom(0));

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir, batchLine + "\r\n", 'P');
        });
    }

    [Fact]
    public void HostRequestedBatch_PipelineSupportsInputRedirectionOnFirstSegment() {
        WithTempDirectory("dos_batch_pipe_input_first", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "PASS.COM", BuildStdinToStdoutCom());
            CreateBinaryFile(tempDir, "CONS.COM", BuildStdinToVideoWriterCom(0));
            CreateTextFile(tempDir, "IN.TXT", "Q");

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CALL PASS.COM < IN.TXT | CALL CONS.COM\r\n", 'Q');
        });
    }

    [Fact]
    public void HostRequestedBatch_InputRedirectionFeedsStdIn() {
        WithTempDirectory("dos_batch_input_redirection", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "READ.COM", BuildStdinToVideoWriterCom(0));
            CreateTextFile(tempDir, "IN.TXT", "R");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CALL READ.COM < IN.TXT\r\n");

            // Act & Assert
            RunAndAssertVideoCell(startBatchPath, tempDir, 'R');
        });
    }

    [Fact]
    public void HostRequestedBatch_SetUpdatesDosEnvironmentForExpansion() {
        WithTempDirectory("dos_batch_set_env", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WENV.COM", BuildVideoWriterCom('V', 0));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "SET SPICE86_BATCH_VAR=YES\r\nIF \"%SPICE86_BATCH_VAR%\"==\"YES\" CALL WENV.COM\r\n");

            // Act & Assert
            RunAndAssertVideoCell(startBatchPath, tempDir, 'V');
        });
    }

    [Fact]
    public void HostRequestedBatch_SetEmptyAssignmentClearsEnvironmentVariable() {
        WithTempDirectory("dos_batch_set_clear_env", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WCLEAR.COM", BuildVideoWriterCom('C', 0));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "SET SPICE86_BATCH_VAR=YES\r\nSET SPICE86_BATCH_VAR=\r\nIF \"%SPICE86_BATCH_VAR%\"==\"\" CALL WCLEAR.COM\r\n");

            // Act & Assert
            RunAndAssertVideoCell(startBatchPath, tempDir, 'C');
        });
    }

    [Fact]
    public void HostRequestedBatch_EchoWritesToRedirectedFile() {
        WithTempDirectory("dos_batch_echo_redirect", tempDir => {
            // Act
            RunBatchScript(tempDir, "ECHO HELLO > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Be("HELLO \r\n");
        });
    }

    [Fact]
    public void HostRequestedBatch_InvalidRedirectionSyntaxDoesNotExecuteCommand() {
        WithTempDirectory("dos_batch_invalid_redirect", tempDir => {
            // Act
            RunBatchScript(tempDir, "ECHO BAD > > OUT.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "OUT.TXT")).Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("ECHO BAD > > OUT.TXT")]
    [InlineData("ECHO BAD >< OUT.TXT")]
    [InlineData("ECHO BAD < < IN.TXT")]
    [InlineData("ECHO BAD || MORE")]
    [InlineData("ECHO BAD > | OUT.TXT")]
    [InlineData("ECHO BAD <| IN.TXT")]
    [InlineData("ECHO BAD | > OUT.TXT")]
    public void HostRequestedBatch_InvalidRedirectionSyntaxCases_DoNotCreateOutput(string batchLine) {
        WithTempDirectory("dos_batch_invalid_redirect_matrix", tempDir => {
            // Act
            RunBatchScript(tempDir, batchLine + "\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "OUT.TXT")).Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("ECHO VALUE>OUT1.TXT>OUT2.TXT", "VALUE\r\n")]
    [InlineData("ECHO VALUE>    OUT1.TXT>     OUT2.TXT", "VALUE\r\n")]
    [InlineData("ECHO VALUE>OUT1.TXT  >OUT2.TXT", "VALUE  \r\n")]
    public void HostRequestedBatch_MultipleOutputRedirections_LastOneWins(string batchLine, string expectedOutput) {
        WithTempDirectory("dos_batch_multi_output", tempDir => {
            // Act
            RunBatchScript(tempDir, batchLine + "\r\n");

            // Assert
            string out2 = File.ReadAllText(Path.Join(tempDir, "OUT2.TXT"));
            out2.Should().Be(expectedOutput);
            File.Exists(Path.Join(tempDir, "OUT1.TXT")).Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("CALL READ.COM < IN1.TXT < IN2.TXT")]
    [InlineData("CALL READ.COM<IN1.TXT<IN2.TXT")]
    public void HostRequestedBatch_MultipleInputRedirections_LastOneWins(string batchLine) {
        WithTempDirectory("dos_batch_multi_input", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "READ.COM", BuildStdinToVideoWriterCom(0));
            CreateTextFile(tempDir, "IN1.TXT", "A");
            CreateTextFile(tempDir, "IN2.TXT", "B");

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir, batchLine + "\r\n", 'B');
        });
    }

    [Fact]
    public void HostRequestedBatch_DoubleInputOperator_LastOneWins() {
        WithTempDirectory("dos_batch_double_input_operator", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "READ.COM", BuildStdinToVideoWriterCom(0));
            CreateTextFile(tempDir, "IN1.TXT", "X");
            CreateTextFile(tempDir, "IN2.TXT", "Y");

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CALL READ.COM << IN1.TXT << IN2.TXT\r\n", 'Y');
        });
    }

    [Theory]
    [InlineData("CALL WRITE.COM | > OUT.TXT")]
    [InlineData("CALL WRITE.COM|>OUT.TXT")]
    [InlineData("CALL WRITE.COM |< IN.TXT")]
    [InlineData("CALL WRITE.COM| < IN.TXT")]
    [InlineData("CALL WRITE.COM < > IN.TXT")]
    public void HostRequestedBatch_InvalidRedirectionSyntaxCases_DoNotLaunchExternalCommand(string batchLine) {
        WithTempDirectory("dos_batch_invalid_redirect_launch_matrix", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITE.COM", BuildVideoWriterCom('Z', 0));
            CreateTextFile(tempDir, "IN.TXT", "I");

            // Act & Assert
            RunAndAssertVideoCellNotWrittenFromScript(tempDir, batchLine + "\r\n", 'Z');
            File.Exists(Path.Join(tempDir, "OUT.TXT")).Should().BeFalse();
        });
    }

    [Fact]
    public void HostRequestedBatch_EchoOffSetsStatusOutput() {
        WithTempDirectory("dos_batch_echo_off_status", tempDir => {
            // Act
            RunBatchScript(tempDir, "ECHO OFF\r\nECHO > STATUS.TXT\r\n");

            // Assert
            string status = File.ReadAllText(Path.Join(tempDir, "STATUS.TXT"));
            status.Should().Be("ECHO is OFF.\r\n");
        });
    }

    [Fact]
    public void HostRequestedBatch_EchoOnAfterOffRestoresStatusOutput() {
        WithTempDirectory("dos_batch_echo_on_status", tempDir => {
            // Act
            RunBatchScript(tempDir, "ECHO OFF\r\nECHO ON\r\nECHO > STATUS.TXT\r\n");

            // Assert
            string status = File.ReadAllText(Path.Join(tempDir, "STATUS.TXT"));
            status.Should().Be("ECHO is ON.\r\n");
        });
    }

    [Theory]
    [InlineData("ECHO. > OUT.TXT", " \r\n")]
    [InlineData("ECHO.HELLO > OUT.TXT", "HELLO \r\n")]
    [InlineData("ECHO.  HELLO > OUT.TXT", "  HELLO \r\n")]
    public void HostRequestedBatch_EchoDotSeparatorOutputsCorrectText(string batchLine, string expectedOutput) {
        WithTempDirectory("dos_batch_echo_dot", tempDir => {
            // Act
            RunBatchScript(tempDir, batchLine + "\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Be(expectedOutput);
        });
    }

    [Fact]
    public void HostRequestedBatch_IfUnquotedComparisonExecutesCommand() {
        WithTempDirectory("dos_batch_if_unquoted", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITE.COM", BuildVideoWriterCom('Q', 0));

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir, "IF ONE==ONE CALL WRITE.COM\r\n", 'Q');
        });
    }

    [Theory]
    [InlineData("FOR %C IN (ONE TWO) ECHO %C")]
    [InlineData("FOR %C (ONE TWO) DO ECHO %C")]
    [InlineData("FOR IN (ONE TWO) DO ECHO %C")]
    [InlineData("FOR %C IN ONE TWO DO ECHO %C")]
    [InlineData("FOR %C IN (ONE TWO) DO")]
    public void HostRequestedBatch_ForMalformedSyntaxSilentlySkips(string batchLine) {
        WithTempDirectory("dos_batch_for_malformed", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITE.COM", BuildVideoWriterCom('Z', 0));

            // Act & Assert
            RunAndAssertVideoCellNotWrittenFromScript(tempDir, batchLine + "\r\n", 'Z');
        });
    }

    [Fact]
    public void HostRequestedBatch_SetWithoutArgumentsEnumeratesEnvironment() {
        WithTempDirectory("dos_batch_set_enumerate", tempDir => {
            // Act
            RunBatchScript(tempDir, "SET MYVAR=MYVALUE\r\nSET > VARS.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "VARS.TXT"));
            output.Should().Contain("MYVAR=MYVALUE");
        });
    }

    [Fact]
    public void HostRequestedBatch_ErrorlevelPropagatesFromCalledProgram() {
        WithTempDirectory("dos_batch_errorlevel_propagate", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "EXIT42.COM", BuildExitCodeCom(42));
            CreateBinaryFile(tempDir, "WRITE.COM", BuildVideoWriterCom('E', 0));

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CALL EXIT42.COM\r\nIF ERRORLEVEL 42 CALL WRITE.COM\r\n", 'E');
        });
    }

    [Fact]
    public void HostRequestedBatch_CallNestedBatchPreservesContext() {
        WithTempDirectory("dos_batch_nested_call", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITE1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "WRITE2.COM", BuildVideoWriterCom('B', 2));
            CreateTextFile(tempDir, "CHILD.BAT", "CALL WRITE2.COM\r\n");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CALL WRITE1.COM\r\nCALL CHILD.BAT\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
        });
    }

    [Fact]
    public void HostRequestedBatch_CallLabel_InvokesInternalSubroutineAndReturns() {
        WithTempDirectory("dos_batch_call_label", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('X', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('Y', 2));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CALL :MYSUB\r\nCALL W2.COM\r\n:MYSUB\r\nCALL W1.COM\r\nGOTO :EOF\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('X');
            cells[1].Should().Be('Y');
        });
    }

    [Fact]
    public void HostRequestedBatch_CallLabel_NestedSubroutineCalls() {
        WithTempDirectory("dos_batch_call_label_nested", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            CreateBinaryFile(tempDir, "W3.COM", BuildVideoWriterCom('C', 4));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CALL :SUB1\r\nCALL W3.COM\r\n:SUB1\r\nCALL W1.COM\r\nCALL :SUB2\r\nGOTO :EOF\r\n:SUB2\r\nCALL W2.COM\r\nGOTO :EOF\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 3);

            // Assert
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
            cells[2].Should().Be('C');
        });
    }

    [Fact]
    public void HostRequestedBatch_CallLabel_WithArguments() {
        WithTempDirectory("dos_batch_call_label_args", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "P1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "P2.COM", BuildVideoWriterCom('B', 2));
            CreateTextFile(tempDir, "MYSUB.BAT", "CALL P1.COM\r\nCALL P2.COM\r\n");
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CALL :TESTS\r\n:TESTS\r\nCALL MYSUB.BAT\r\nGOTO :EOF\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
        });
    }

    [Fact]
    public void HostRequestedBatch_CallLabel_UnknownLabelReturnsToNextLine() {
        WithTempDirectory("dos_batch_call_label_unknown", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CALL :NOTFOUND\r\nCALL W1.COM\r\nCALL W2.COM\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);

            // Assert
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
        });
    }

    [Fact]
    public void HostRequestedBatch_ForWithWildcardExpandsMatchingFiles() {
        WithTempDirectory("dos_batch_for_wildcard", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            CreateTextFile(tempDir, "ALPHA.TXT", "alpha");
            CreateTextFile(tempDir, "BETA.TXT", "beta");
            CreateTextFile(tempDir, "ROUTE.BAT",
                "IF \"%1\"==\"ALPHA.TXT\" CALL W1.COM\r\nIF \"%1\"==\"BETA.TXT\" CALL W2.COM\r\n");

            // Act & Assert
            RunAndAssertVideoCellsFromScript(tempDir,
                "FOR %%I IN (*.TXT) DO CALL ROUTE.BAT %%I\r\n",
                new[] { 'A', 'B' });
        });
    }

    /// <summary>
    /// FOR with wildcard + TYPE + redirect appends each matching file to output.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ForWithWildcardTypeRedirect() {
        WithTempDirectory("dos_batch_for_type_redir", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TXT", "aaa");
            CreateTextFile(tempDir, "B.TXT", "bbb");
            CreateTextFile(tempDir, "C.DAT", "ccc");

            // Act
            RunBatchScript(tempDir, "FOR %%I IN (*.TXT) DO TYPE %%I >> OUT.TXT\r\n");

            // Assert — OUT.TXT should contain both .TXT files
            string outPath = Path.Join(tempDir, "OUT.TXT");
            File.Exists(outPath).Should().BeTrue("FOR TYPE >> should create the output file");
            string content = File.ReadAllText(outPath);
            content.Should().Contain("aaa");
            content.Should().Contain("bbb");
            content.Should().NotContain("ccc");
        });
    }

    [Fact]
    public void HostRequestedBatch_PathCommandSetsEnvironmentAndExpandsInIf() {
        WithTempDirectory("dos_batch_path_cmd", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WPATH.COM", BuildVideoWriterCom('P', 0));

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "PATH C:\\TOOLS\r\nIF \"%PATH%\"==\"C:\\TOOLS\" CALL WPATH.COM\r\n", 'P');
        });
    }

    [Fact]
    public void HostRequestedBatch_PauseDoesNotBlockWithRedirectedStdinAndContinues() {
        WithTempDirectory("dos_batch_pause", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "KEY.TXT", " ");

            // Act
            RunBatchScript(tempDir,
                "ECHO BEFORE> BEFORE.TXT\r\nPAUSE < KEY.TXT\r\nECHO AFTER> AFTER.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "BEFORE.TXT")).Should().BeTrue();
            File.Exists(Path.Join(tempDir, "AFTER.TXT")).Should().BeTrue();
        });
    }

    [Fact]
    public void HostRequestedBatch_ForWildcardWithNoMatches_Skips() {
        WithTempDirectory("dos_batch_for_wildcard_nomatch", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITE.COM", BuildVideoWriterCom('Z', 0));

            // Act & Assert
            RunAndAssertVideoCellNotWrittenFromScript(tempDir,
                "FOR %%I IN (*.DAT) DO CALL WRITE.COM\r\n", 'Z');
        });
    }

    [Fact]
    public void HostRequestedBatch_ForMixesLiteralsAndWildcards() {
        WithTempDirectory("dos_batch_for_mixed", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('M', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('N', 2));
            CreateTextFile(tempDir, "DATA.TXT", "data");
            CreateTextFile(tempDir, "ROUTE.BAT",
                "IF \"%1\"==\"DATA.TXT\" CALL W1.COM\r\nIF \"%1\"==\"LITERAL\" CALL W2.COM\r\n");

            // Act & Assert
            RunAndAssertVideoCellsFromScript(tempDir,
                "FOR %%I IN (*.TXT LITERAL) DO CALL ROUTE.BAT %%I\r\n",
                new[] { 'M', 'N' });
        });
    }

    [Fact]
    public void HostRequestedBatch_VariableExpansionWorksAcrossBatchCalls() {
        WithTempDirectory("dos_batch_var_expand_across", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "CHILD.BAT", "ECHO %SHAREDVAR% > OUTPUT.TXT\r\n");

            // Act
            RunBatchScript(tempDir, "SET SHAREDVAR=SHARED_VALUE\r\nCALL CHILD.BAT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUTPUT.TXT"));
            output.Should().Contain("SHARED_VALUE");
        });
    }

    [Fact]
    public void HostRequestedBatch_ClsClearsScreenAndContinuesExecution() {
        WithTempDirectory("dos_batch_cls", tempDir => {
            // Arrange: write 'X' at cell 0, then CLS clears screen, then write 'Y' at cell 2.
            CreateBinaryFile(tempDir, "WRITE_X.COM", BuildVideoWriterCom('X', 0));
            CreateBinaryFile(tempDir, "WRITE_Y.COM", BuildVideoWriterCom('Y', 4));

            // Act
            char[] cells = RunAndCaptureVideoCells(
                CreateTextFile(tempDir, "START.BAT",
                    "CALL WRITE_X.COM\r\nCLS\r\nCALL WRITE_Y.COM\r\n"),
                tempDir, 3);

            // Assert: cell 0 was cleared by CLS (replaced with space), cell 2 proves execution continued.
            cells[0].Should().Be(' ', "CLS should have cleared the screen");
            cells[2].Should().Be('Y', "execution should continue after CLS");
        });
    }


    [Fact]
    public void HostRequestedBatch_ChoiceDefaultYn_SelectsY_SetsErrorlevel1() {
        WithTempDirectory("dos_batch_choice_y", tempDir => {
            // Arrange: stdin contains 'Y', CHOICE /C:YN should set ERRORLEVEL=1
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('Y', 0));
            CreateTextFile(tempDir, "KEY.TXT", "Y");

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CHOICE /C:YN < KEY.TXT\r\nIF ERRORLEVEL 1 CALL W1.COM\r\n", 'Y');
        });
    }

    [Fact]
    public void HostRequestedBatch_ChoiceDefaultYn_SelectsN_SetsErrorlevel2() {
        WithTempDirectory("dos_batch_choice_n", tempDir => {
            // Arrange: stdin contains 'N', CHOICE /C:YN should set ERRORLEVEL=2
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('N', 0));
            CreateBinaryFile(tempDir, "SKIP.COM", BuildVideoWriterCom('S', 2));
            CreateTextFile(tempDir, "KEY.TXT", "N");

            // Act
            char[] cells = RunAndCaptureVideoCells(
                CreateTextFile(tempDir, "START.BAT",
                    "CHOICE /C:YN < KEY.TXT\r\n" +
                    "IF ERRORLEVEL 2 CALL W2.COM\r\n" +
                    "IF ERRORLEVEL 1 CALL SKIP.COM\r\n"),
                tempDir, 2);

            // Assert: ERRORLEVEL=2, so both "IF ERRORLEVEL 2" and "IF ERRORLEVEL 1" match
            // (DOS IF ERRORLEVEL checks >=), but W2 runs first at offset 0
            cells[0].Should().Be('N');
        });
    }

    [Fact]
    public void HostRequestedBatch_ChoiceCustomKeys_SelectsThirdOption() {
        WithTempDirectory("dos_batch_choice_custom", tempDir => {
            // Arrange: CHOICE /C:ABC with 'C' → ERRORLEVEL=3
            CreateBinaryFile(tempDir, "W3.COM", BuildVideoWriterCom('3', 0));
            CreateTextFile(tempDir, "KEY.TXT", "C");

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CHOICE /C:ABC < KEY.TXT\r\nIF ERRORLEVEL 3 CALL W3.COM\r\n", '3');
        });
    }

    [Fact]
    public void HostRequestedBatch_ChoiceCaseInsensitiveByDefault() {
        WithTempDirectory("dos_batch_choice_case_insensitive", tempDir => {
            // Arrange: lowercase 'b' should match 'B' in /C:ABC (default is case-insensitive)
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('2', 0));
            CreateTextFile(tempDir, "KEY.TXT", "b");

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CHOICE /C:ABC < KEY.TXT\r\nIF ERRORLEVEL 2 CALL W2.COM\r\n", '2');
        });
    }

    [Fact]
    public void HostRequestedBatch_ChoiceWithSFlag_IsCaseSensitive() {
        WithTempDirectory("dos_batch_choice_case_sensitive", tempDir => {
            // Arrange: /S makes CHOICE case-sensitive; 'a' should NOT match 'A'
            // Preload 'a' (no match) then 'B' (matches choice #2) → ERRORLEVEL=2
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('1', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('2', 0));

            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CHOICE /S /C:AB\r\n" +
                "IF ERRORLEVEL 2 W2.COM\r\n" +
                "IF ERRORLEVEL 1 W1.COM\r\n");

            // Pre-load 'a' (scan=0x1E, ascii=0x61) then 'B' (scan=0x30, ascii=0x42)
            ushort[] keys = new ushort[] { 0x1E61, 0x3042 };

            // Act
            char[] cells = RunWithPreloadedKeysAndCaptureVideoCells(startBatchPath, tempDir, 1, keys);

            // Assert: 'a' skipped (case-sensitive), 'B' → ERRORLEVEL 2 → W2.COM writes '2'
            cells[0].Should().Be('2');
        });
    }

    [Fact]
    public void HostRequestedBatch_ChoiceNoChoiceArg_DefaultsToYN() {
        WithTempDirectory("dos_batch_choice_default_yn", tempDir => {
            // Arrange: CHOICE without /C defaults to Y,N choices
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('1', 0));
            CreateTextFile(tempDir, "KEY.TXT", "Y");

            // Act & Assert: 'Y' matches first choice → ERRORLEVEL=1
            RunAndAssertVideoCellFromScript(tempDir,
                "CHOICE < KEY.TXT\r\nIF ERRORLEVEL 1 CALL W1.COM\r\n", '1');
        });
    }

    [Fact]
    public void HostRequestedBatch_ChoiceWithNFlag_PromptWrittenToRedirectedFile() {
        WithTempDirectory("dos_batch_choice_n_flag", tempDir => {
            // Arrange: /N suppresses the [Y,N]? prompt, but text message should still show
            CreateTextFile(tempDir, "KEY.TXT", "Y");

            // Act: redirect stdout to capture prompt output with /N
            RunBatchScript(tempDir,
                "CHOICE /N /C:YN Choose: > PROMPT.TXT < KEY.TXT\r\n");

            // Assert: with /N flag, prompt should NOT contain [Y,N]? but may contain "Choose:"
            string prompt = System.IO.File.ReadAllText(System.IO.Path.Join(tempDir, "PROMPT.TXT"));
            prompt.Should().NotContain("[Y,N]?");
        });
    }

    [Fact]
    public void HostRequestedBatch_ChoiceWithPromptText_WritesPrompt() {
        WithTempDirectory("dos_batch_choice_prompt", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "KEY.TXT", "Y");

            // Act: CHOICE with text message and default choices
            RunBatchScript(tempDir,
                "CHOICE /C:YN Continue > PROMPT.TXT < KEY.TXT\r\n");

            // Assert: prompt should include the text and [Y,N]?
            string prompt = System.IO.File.ReadAllText(System.IO.Path.Join(tempDir, "PROMPT.TXT"));
            prompt.Should().Contain("[Y,N]?");
        });
    }

    /// <summary>
    /// CHOICE /T:Y,5 with no keyboard input should auto-select Y (timeout default), setting ERRORLEVEL=1.
    /// The COM stub for /T exits immediately with the default choice's errorlevel.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ChoiceTimeout_SelectsDefaultOnEof() {
        WithTempDirectory("dos_batch_choice_timeout", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('T', 0));

            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CHOICE /C:YN /T:Y,5\r\nIF ERRORLEVEL 1 CALL W1.COM\r\n");

            // Act: /T:Y,5 → default=Y → ERRORLEVEL=1 (no key needed, immediate exit)
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 1);

            // Assert: timeout default Y → ERRORLEVEL 1 → W1.COM writes 'T'
            cells[0].Should().Be('T');
        });
    }

    /// <summary>
    /// CHOICE /T:B,3 with actual keypress should use the pressed key, not the default.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ChoiceTimeout_KeypressTakesPrecedence() {
        WithTempDirectory("dos_batch_choice_timeout_key", tempDir => {
            // Arrange: stdin has 'A', so CHOICE should pick 'A' (ERRORLEVEL=1), not timeout default 'B'
            CreateBinaryFile(tempDir, "WA.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "WB.COM", BuildVideoWriterCom('B', 2));
            CreateTextFile(tempDir, "KEY.TXT", "A");

            // Act
            char[] cells = RunAndCaptureVideoCells(
                CreateTextFile(tempDir, "START.BAT",
                    "CHOICE /C:AB /T:B,3 < KEY.TXT\r\n" +
                    "IF ERRORLEVEL 2 CALL WB.COM\r\n" +
                    "IF ERRORLEVEL 1 CALL WA.COM\r\n"),
                tempDir, 2);

            // Assert: A pressed → ERRORLEVEL=1, WA runs
            cells[0].Should().Be('A');
        });
    }


    [Fact]
    public void HostRequestedBatch_JillMenuStyle_ChoiceRoutesViaErrorlevelGotoChain() {
        WithTempDirectory("dos_batch_jill_menu", tempDir => {
            // Arrange: simulate a classic DOS game batch menu like Jill of the Jungle
            CreateBinaryFile(tempDir, "GAME.COM", BuildVideoWriterCom('G', 0));
            CreateBinaryFile(tempDir, "STORY.COM", BuildVideoWriterCom('S', 2));
            CreateBinaryFile(tempDir, "ORDER.COM", BuildVideoWriterCom('O', 4));
            CreateBinaryFile(tempDir, "EXIT.COM", BuildExitCodeCom(0));
            CreateTextFile(tempDir, "KEY.TXT", "2");

            string menuScript =
                "@ECHO OFF\r\n" +
                "ECHO  1. Start Game\r\n" +
                "ECHO  2. Read Story\r\n" +
                "ECHO  3. Ordering Info\r\n" +
                "ECHO  4. Quit\r\n" +
                "CHOICE /C:1234 /N < KEY.TXT\r\n" +
                "IF ERRORLEVEL 4 GOTO QUIT\r\n" +
                "IF ERRORLEVEL 3 GOTO ORDER\r\n" +
                "IF ERRORLEVEL 2 GOTO STORY\r\n" +
                "IF ERRORLEVEL 1 GOTO GAME\r\n" +
                ":GAME\r\n" +
                "CALL GAME.COM\r\n" +
                "GOTO END\r\n" +
                ":STORY\r\n" +
                "CALL STORY.COM\r\n" +
                "GOTO END\r\n" +
                ":ORDER\r\n" +
                "CALL ORDER.COM\r\n" +
                "GOTO END\r\n" +
                ":QUIT\r\n" +
                "CALL EXIT.COM\r\n" +
                ":END\r\n";

            // Act: user presses '2' (Read Story)
            char[] cells = RunAndCaptureVideoCells(
                CreateTextFile(tempDir, "START.BAT", menuScript), tempDir, 3);

            // Assert: '2' matched → ERRORLEVEL=2 → GOTO STORY → STORY.COM writes 'S' at offset 2
            cells[0].Should().NotBe('G', "should not reach GAME");
            cells[1].Should().Be('S', "should reach STORY (choice 2)");
            cells[2].Should().NotBe('O', "should not reach ORDER");
        });
    }


    /// <summary>
    /// TYPE displays the contents of an existing file to stdout.
    /// Redirects stdout to OUT.TXT and verifies the file contents appear there.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Type_DisplaysFileContents() {
        WithTempDirectory("dos_batch_type", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "HELLO.TXT", "Hello World");

            // Act
            RunBatchScript(tempDir, "TYPE HELLO.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Be("Hello World");
        });
    }

    /// <summary>
    /// TYPE on a nonexistent file writes a "File not found" error message.
    /// Redirects stdout to OUT.TXT and verifies the error message.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Type_MissingFile_WritesError() {
        WithTempDirectory("dos_batch_type_missing", tempDir => {
            // Arrange — no file created

            // Act
            RunBatchScript(tempDir, "TYPE NOFILE.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Contain("not found", "TYPE on a missing file should report an error");
        });
    }


    /// <summary>
    /// CD with no arguments prints the current directory to stdout.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Cd_NoArgs_PrintsCurrentDirectory() {
        WithTempDirectory("dos_batch_cd_noargs", tempDir => {
            // Act
            RunBatchScript(tempDir, "CD > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().StartWith("C:\\", "CD with no args should print the current drive and directory");
        });
    }

    /// <summary>
    /// CD with a path changes the current directory. A subsequent CD with no args
    /// should print the new directory.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Cd_ChangesDirectory_ThenPrintsNew() {
        WithTempDirectory("dos_batch_cd_change", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "SUBDIR");

            // Act
            RunBatchScript(tempDir, "CD SUBDIR\r\nCD > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "SUBDIR", "OUT.TXT")).Trim();
            output.Should().Contain("SUBDIR", "CD should have changed to the subdirectory");
        });
    }

    /// <summary>
    /// CD.. (no space) navigates to the parent directory, just like CD ..
    /// </summary>
    [Fact]
    public void HostRequestedBatch_CdDotDot_NavigatesToParent() {
        WithTempDirectory("dos_batch_cd_dotdot", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "CHILD");

            // Act: enter CHILD, then CD.. to go back up
            RunBatchScript(tempDir, "CD CHILD\r\nCD..\r\nCD > OUT.TXT\r\n");

            // Assert: should be back at root, not in CHILD
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotContain("CHILD", "CD.. should navigate to the parent directory");
        });
    }

    /// <summary>
    /// CD. (no space) stays in the current directory, just like CD .
    /// </summary>
    [Fact]
    public void HostRequestedBatch_CdDot_StaysInCurrentDirectory() {
        WithTempDirectory("dos_batch_cd_dot", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "SUB");

            // Act: enter SUB, then CD. which should stay put
            RunBatchScript(tempDir, "CD SUB\r\nCD.\r\nCD > OUT.TXT\r\n");

            // Assert: should still be in SUB
            string output = File.ReadAllText(Path.Join(tempDir, "SUB", "OUT.TXT")).Trim();
            output.Should().Contain("SUB", "CD. should stay in the current directory");
        });
    }

    /// <summary>
    /// CD\ (no space) navigates to the root directory.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_CdBackslash_NavigatesToRoot() {
        WithTempDirectory("dos_batch_cd_backslash", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "DEEP");

            // Act: enter DEEP, then CD\ to go to root
            RunBatchScript(tempDir, "CD DEEP\r\nCD\\\r\nCD > OUT.TXT\r\n");

            // Assert: should be at root
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotContain("DEEP", "CD\\ should navigate to the root directory");
        });
    }


    /// <summary>
    /// IF string comparison is case-sensitive, matching DOS strcmp behavior.
    /// "hello"=="HELLO" should NOT match, but "hello"=="hello" should.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_IfStringComparison_IsCaseSensitive() {
        WithTempDirectory("dos_batch_if_case", tempDir => {
            // Arrange — MISS.COM writes 'M' at offset 0 if the case-insensitive match wrongly fires.
            // EXACT.COM writes 'E' at offset 0 if the exact-case match correctly fires.
            CreateBinaryFile(tempDir, "MISS.COM", BuildVideoWriterCom('M', 0));
            CreateBinaryFile(tempDir, "EXACT.COM", BuildVideoWriterCom('E', 0));

            // Act — first IF should NOT match (different case), second should match (same case)
            // Because both write to offset 0, only the last matching one determines the value.
            // If case-insensitive: 'M' then 'E'. If case-sensitive: only 'E'.
            // We verify the first IF does not fire by checking no 'M' appears at offset 2.
            CreateBinaryFile(tempDir, "BAD.COM", BuildVideoWriterCom('B', 2));
            string script =
                "IF hello==HELLO CALL BAD.COM\r\n" +
                "IF hello==hello CALL EXACT.COM\r\n";

            // Assert — offset 0 should be 'E' (exact match); offset 2 should NOT be 'B'
            string startBatchPath = CreateTextFile(tempDir, "START.BAT", script);
            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().Be('E', "exact case match should execute");
            cells[1].Should().NotBe('B', "case-insensitive match should NOT execute");
        });
    }

    /// <summary>
    /// IF comparison preserves surrounding quotes as part of the token, matching DOS behavior.
    /// IF "A"=="A" should match because both sides are the literal string including quotes.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_IfStringComparison_PreservesQuotes() {
        WithTempDirectory("dos_batch_if_quotes", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "WRITE.COM", BuildVideoWriterCom('W', 0));

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "IF \"A B\"==\"A B\" CALL WRITE.COM\r\n", 'W');
        });
    }


    /// <summary>
    /// GOTO with a nonexistent label should stop the current batch file execution.
    /// Commands after the GOTO should not run.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_GotoMissingLabel_StopsBatchExecution() {
        WithTempDirectory("dos_batch_goto_missing", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "NEVER.COM", BuildVideoWriterCom('N', 0));

            // Act & Assert
            RunAndAssertVideoCellNotWrittenFromScript(tempDir,
                "GOTO NONEXIST\r\nCALL NEVER.COM\r\n", 'N');
        });
    }


    /// <summary>
    /// TYPE should stop reading at the 0x1A (Ctrl-Z) EOF marker, matching DOS behavior.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Type_StopsAtCtrlZEof() {
        WithTempDirectory("dos_batch_type_eof", tempDir => {
            // Arrange — file has "AB" then 0x1A then "CD"
            byte[] content = new byte[] { 0x41, 0x42, 0x1A, 0x43, 0x44 };
            CreateBinaryFile(tempDir, "DATA.TXT", content);

            // Act
            RunBatchScript(tempDir, "TYPE DATA.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Be("AB", "TYPE should stop at 0x1A EOF marker");
        });
    }


    /// <summary>
    /// CHOICE should skip invalid keys and accept the first valid key, matching DOSBox retry loop.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ChoiceSkipsInvalidKeysAndAcceptsFirstValid() {
        WithTempDirectory("dos_batch_choice_retry", tempDir => {
            // Arrange: stdin contains 'X' (invalid), then 'N' (valid for YN choices)
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('2', 0));
            CreateTextFile(tempDir, "KEY.TXT", "XN");

            // Act & Assert: 'X' is not in YN, should be skipped; 'N' matches → ERRORLEVEL=2
            RunAndAssertVideoCellFromScript(tempDir,
                "CHOICE /C:YN < KEY.TXT\r\nIF ERRORLEVEL 2 CALL W2.COM\r\n", '2');
        });
    }


    /// <summary>
    /// ECHO OFF in an inner CALL'd batch should not affect the outer batch's echo state.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_EchoOffInInnerBatchDoesNotAffectOuter() {
        WithTempDirectory("dos_batch_echo_per_batch", tempDir => {
            // Arrange: INNER.BAT sets ECHO OFF
            CreateTextFile(tempDir, "INNER.BAT", "@ECHO OFF\r\n");

            // Act: outer batch checks ECHO status after calling inner
            RunBatchScript(tempDir,
                "ECHO ON\r\nCALL INNER.BAT\r\nECHO > STATUS.TXT\r\n");

            // Assert: outer batch still has ECHO ON
            string status = File.ReadAllText(Path.Join(tempDir, "STATUS.TXT"));
            status.Should().Be("ECHO is ON.\r\n");
        });
    }


    /// <summary>
    /// Batch file reader should stop at the 0x1A (Ctrl-Z) EOF marker, so lines after it are not executed.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_BatchFileReadingStopsAtCtrlZ() {
        WithTempDirectory("dos_batch_read_eof", tempDir => {
            // Arrange: batch file has ECHO HELLO, then 0x1A, then ECHO WORLD
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('H', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('W', 2));
            byte[] batchContent = System.Text.Encoding.ASCII.GetBytes(
                "CALL W1.COM\r\n" +
                "\x1a" +
                "CALL W2.COM\r\n");
            CreateBinaryFile(tempDir, "TEST.BAT", batchContent);

            // Act
            char[] cells = RunAndCaptureVideoCells(
                Path.Join(tempDir, "TEST.BAT"), tempDir, 2);

            // Assert: W1 should run, W2 should NOT (after EOF marker)
            cells[0].Should().Be('H');
            cells[1].Should().NotBe('W');
        });
    }


    /// <summary>
    /// EXIT terminates the current batch file; subsequent commands are not executed.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Exit_StopsExecution() {
        WithTempDirectory("dos_batch_exit", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            string startBat = CreateTextFile(tempDir, "START.BAT",
                "W1\r\nEXIT\r\nW2\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBat, tempDir, 2);

            // Assert: W1 should run, W2 should NOT run because EXIT stopped processing
            cells[0].Should().Be('A');
            cells[1].Should().NotBe('B');
        });
    }

    /// <summary>
    /// EXIT inside a CALLed batch file terminates all batch processing, not just the inner file.
    /// DOSBox sets exit_cmd_called which stops the entire shell.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Exit_InsideCall_StopsAllBatches() {
        WithTempDirectory("dos_batch_exit_call", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            CreateTextFile(tempDir, "INNER.BAT", "W1\r\nEXIT\r\n");
            string startBat = CreateTextFile(tempDir, "START.BAT",
                "CALL INNER.BAT\r\nW2\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBat, tempDir, 2);

            // Assert: W1 runs (inside INNER), W2 should NOT run because EXIT cleared all contexts
            cells[0].Should().Be('A');
            cells[1].Should().NotBe('B');
        });
    }


    /// <summary>
    /// Running a .BAT without CALL should replace the current batch (tail-call).
    /// Commands after the bare batch invocation in the parent should NOT execute.
    /// DOSBox pops the current batch before pushing the new one.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_BareBatInvocation_ReplacesCurrentBatch() {
        WithTempDirectory("dos_batch_tailcall", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('A', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('B', 2));
            // SUB.BAT writes 'A' at cell 0
            CreateTextFile(tempDir, "SUB.BAT", "W1\r\n");

            // START.BAT invokes SUB.BAT WITHOUT CALL, then tries to run W2.
            // In DOS, the bare invocation replaces START.BAT, so W2 should never run.
            string startBat = CreateTextFile(tempDir, "START.BAT",
                "SUB.BAT\r\nW2\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBat, tempDir, 2);

            // Assert: W1 runs (from SUB.BAT), W2 should NOT run (tail-call replacement)
            cells[0].Should().Be('A');
            cells[1].Should().NotBe('B',
                "bare .BAT invocation should replace current batch; W2 should not execute");
        });
    }

    /// <summary>
    /// Bare invocation of a missing .BAT should not drop the current context;
    /// following lines in the same batch should still execute.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_BareMissingBat_DoesNotDropCurrentContext() {
        WithTempDirectory("dos_batch_tailcall_missing", tempDir => {
            // Arrange
            CreateBinaryFile(tempDir, "PASS.COM", BuildVideoWriterCom('P', 0));
            string startBat = CreateTextFile(tempDir, "START.BAT",
                "MISSING.BAT\r\nCALL PASS.COM\r\n");

            // Act
            char[] cells = RunAndCaptureVideoCells(startBat, tempDir, 1);

            // Assert
            cells[0].Should().Be('P',
                "missing bare batch invocation should not abort remaining parent batch lines");
        });
    }

    /// <summary>
    /// FOR parser should ignore ')' inside quoted list items.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ForList_QuotedRightParenthesis_IsParsedCorrectly() {
        WithTempDirectory("dos_batch_for_quoted_paren", tempDir => {
            // Act
            RunBatchScript(tempDir, "FOR %%I IN (\"A B\" \"C,D\" \"E;F\" \"A)B\") DO ECHO %%I>>OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"));
            output.Should().Contain("\"A B\"");
            output.Should().Contain("\"C,D\"");
            output.Should().Contain("\"E;F\"");
            output.Should().Contain("A)B");
        });
    }


    /// <summary>
    /// LH passes through to execute the rest of the command line.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Lh_PassesThrough() {
        WithTempDirectory("dos_batch_lh", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "SRC.TXT", "lh content");

            // Act — LH should be stripped and TYPE should execute normally
            RunBatchScript(tempDir, "LH TYPE SRC.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().Be("lh content");
        });
    }

    /// <summary>
    /// LOADHIGH (full keyword) also passes through to execute the rest of the command line.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Loadhigh_PassesThrough() {
        WithTempDirectory("dos_batch_loadhigh", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "SRC.TXT", "loadhigh content");

            // Act
            RunBatchScript(tempDir, "LOADHIGH TYPE SRC.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().Be("loadhigh content");
        });
    }


    /// <summary>
    /// MD creates a directory and CD can enter it.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Md_CreatesDirectory() {
        WithTempDirectory("dos_batch_md", tempDir => {
            // Act
            RunBatchScript(tempDir, "MD NEWDIR\r\nCD NEWDIR\r\nCD > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "NEWDIR", "OUT.TXT")).Trim();
            output.Should().Contain("NEWDIR", "MD should have created the directory");
        });
    }

    /// <summary>
    /// MKDIR is an alias for MD.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Mkdir_IsAliasForMd() {
        WithTempDirectory("dos_batch_mkdir", tempDir => {
            // Act
            RunBatchScript(tempDir, "MKDIR SUBDIR\r\nCD SUBDIR\r\nCD > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "SUBDIR", "OUT.TXT")).Trim();
            output.Should().Contain("SUBDIR");
        });
    }

    /// <summary>
    /// MD on an already existing directory prints an error.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Md_ExistingDirectory_PrintsError() {
        WithTempDirectory("dos_batch_md_exists", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "EXISTING");

            // Act
            RunBatchScript(tempDir, "MD EXISTING > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty("MD on an existing directory should report an error");
        });
    }


    /// <summary>
    /// RD removes an empty directory.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Rd_RemovesEmptyDirectory() {
        WithTempDirectory("dos_batch_rd", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "TODEL");

            // Act
            RunBatchScript(tempDir, "RD TODEL\r\n");

            // Assert
            Directory.Exists(Path.Join(tempDir, "TODEL")).Should().BeFalse(
                "RD should remove the empty directory");
        });
    }

    /// <summary>
    /// RMDIR is an alias for RD.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Rmdir_IsAliasForRd() {
        WithTempDirectory("dos_batch_rmdir", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "GONE");

            // Act
            RunBatchScript(tempDir, "RMDIR GONE\r\n");

            // Assert
            Directory.Exists(Path.Join(tempDir, "GONE")).Should().BeFalse();
        });
    }

    /// <summary>
    /// RD on a non-existent directory prints an error.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Rd_NonExistent_PrintsError() {
        WithTempDirectory("dos_batch_rd_missing", tempDir => {
            // Act
            RunBatchScript(tempDir, "RD NOPE > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty("RD on a non-existent directory should report an error");
        });
    }


    /// <summary>
    /// DEL removes a single file.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Del_RemovesSingleFile() {
        WithTempDirectory("dos_batch_del", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "KILL.TXT", "doomed");

            // Act
            RunBatchScript(tempDir, "DEL KILL.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "KILL.TXT")).Should().BeFalse(
                "DEL should remove the specified file");
        });
    }

    /// <summary>
    /// ERASE is an alias for DEL.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Erase_IsAliasForDel() {
        WithTempDirectory("dos_batch_erase", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "GONE.TXT", "bye");

            // Act
            RunBatchScript(tempDir, "ERASE GONE.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "GONE.TXT")).Should().BeFalse();
        });
    }

    /// <summary>
    /// DEL with wildcard removes all matching files.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Del_Wildcard_RemovesMatchingFiles() {
        WithTempDirectory("dos_batch_del_wild", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TMP", "a");
            CreateTextFile(tempDir, "B.TMP", "b");
            CreateTextFile(tempDir, "KEEP.TXT", "keep");

            // Act
            RunBatchScript(tempDir, "DEL *.TMP\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "A.TMP")).Should().BeFalse();
            File.Exists(Path.Join(tempDir, "B.TMP")).Should().BeFalse();
            File.Exists(Path.Join(tempDir, "KEEP.TXT")).Should().BeTrue(
                "DEL *.TMP should not remove .TXT files");
        });
    }

    /// <summary>
    /// DEL on a non-existent file prints "File not found".
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Del_NonExistent_PrintsError() {
        WithTempDirectory("dos_batch_del_missing", tempDir => {
            // Act
            RunBatchScript(tempDir, "DEL NOFILE.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().Contain("not found", "DEL on a missing file should report file not found");
        });
    }

    /// <summary>
    /// DEL skips directories (does not delete them).
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Del_SkipsDirectories() {
        WithTempDirectory("dos_batch_del_skipdir", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "SUBDIR");
            CreateTextFile(tempDir, "FILE.TXT", "file");

            // Act — delete only .TXT files, which should leave directories intact
            RunBatchScript(tempDir, "DEL *.TXT\r\n");

            // Assert
            Directory.Exists(Path.Join(tempDir, "SUBDIR")).Should().BeTrue(
                "DEL should not remove directories");
            File.Exists(Path.Join(tempDir, "FILE.TXT")).Should().BeFalse(
                "DEL *.TXT should remove the .TXT file");
        });
    }

    /// <summary>
    /// DEL skips read-only files (DOS attribute 0x01). DOSBox's CMD_DELETE checks
    /// FatAttributeFlags before removing.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Del_SkipsReadonlyFiles() {
        WithTempDirectory("dos_batch_del_readonly", tempDir => {
            // Arrange
            string roFile = CreateTextFile(tempDir, "LOCKED.TXT", "protected");
            File.SetAttributes(roFile, FileAttributes.ReadOnly);
            CreateTextFile(tempDir, "NORMAL.TXT", "deletable");

            try {
                // Act
                RunBatchScript(tempDir, "DEL *.TXT\r\n");

                // Assert
                File.Exists(Path.Join(tempDir, "LOCKED.TXT")).Should().BeTrue(
                    "DEL should skip read-only files");
                File.Exists(Path.Join(tempDir, "NORMAL.TXT")).Should().BeFalse(
                    "DEL should remove normal files");
            } finally {
                // Cleanup: remove readonly attribute so temp directory cleanup works
                if (File.Exists(roFile)) {
                    File.SetAttributes(roFile, FileAttributes.Normal);
                }
            }
        });
    }


    /// <summary>
    /// REN renames a single file.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Ren_RenamesSingleFile() {
        WithTempDirectory("dos_batch_ren", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "OLD.TXT", "content");

            // Act
            RunBatchScript(tempDir, "REN OLD.TXT NEW.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "OLD.TXT")).Should().BeFalse();
            File.Exists(Path.Join(tempDir, "NEW.TXT")).Should().BeTrue();
            File.ReadAllText(Path.Join(tempDir, "NEW.TXT")).Should().Be("content");
        });
    }

    /// <summary>
    /// RENAME is an alias for REN.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Rename_IsAliasForRen() {
        WithTempDirectory("dos_batch_rename", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TXT", "data");

            // Act
            RunBatchScript(tempDir, "RENAME A.TXT B.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "A.TXT")).Should().BeFalse();
            File.Exists(Path.Join(tempDir, "B.TXT")).Should().BeTrue();
        });
    }

    /// <summary>
    /// REN on a non-existent source file prints an error.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Ren_NonExistent_PrintsError() {
        WithTempDirectory("dos_batch_ren_missing", tempDir => {
            // Act
            RunBatchScript(tempDir, "REN NOFILE.TXT NEW.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty("REN on a missing file should report an error");
        });
    }

    /// <summary>
    /// REN with only one argument (missing target) prints a syntax error.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Ren_MissingTarget_PrintsError() {
        WithTempDirectory("dos_batch_ren_noarg", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "FILE.TXT", "data");

            // Act
            RunBatchScript(tempDir, "REN FILE.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty("REN with missing target should report a syntax error");
        });
    }

    // ───────────────────────── RENAME wildcard expansion ─────────────────────────

    /// <summary>
    /// REN *.TXT *.BAK expands wildcard source and batch-renames matching files.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Ren_WildcardSourceTarget() {
        WithTempDirectory("dos_batch_ren_wildcard", tempDir => {
            // Arrange — create three .TXT files
            CreateTextFile(tempDir, "A.TXT", "aaa");
            CreateTextFile(tempDir, "B.TXT", "bbb");
            CreateTextFile(tempDir, "C.DAT", "ccc");

            // Act — REN *.TXT *.BAK should rename matching files
            RunBatchScript(tempDir, "REN *.TXT *.BAK\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "A.TXT")).Should().BeFalse();
            File.Exists(Path.Join(tempDir, "A.BAK")).Should().BeTrue();
            File.Exists(Path.Join(tempDir, "B.TXT")).Should().BeFalse();
            File.Exists(Path.Join(tempDir, "B.BAK")).Should().BeTrue();
            File.Exists(Path.Join(tempDir, "C.DAT")).Should().BeTrue("non-matching file should remain");
        });
    }

    /// <summary>
    /// REN with wildcard and no matches silently does nothing.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Ren_WildcardNoMatches() {
        WithTempDirectory("dos_batch_ren_wild_none", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "DATA.DAT", "data");

            // Act
            RunBatchScript(tempDir, "REN *.TXT *.BAK\r\nECHO DONE > DONE.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "DONE.TXT")).Should().BeTrue("batch should continue");
            File.Exists(Path.Join(tempDir, "DATA.DAT")).Should().BeTrue();
        });
    }

    // ───────────────────────── VOL drive argument ─────────────────────────

    /// <summary>
    /// VOL C: shows volume information for drive C.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Vol_WithDriveArgument() {
        WithTempDirectory("dos_batch_vol_drive", tempDir => {
            // Act
            RunBatchScript(tempDir, "VOL C: > VOL.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "VOL.TXT"));
            output.Should().NotBeEmpty("VOL C: should produce output");
        });
    }

    // ───────────────────────── FOR wildcard expansion ─────────────────────────

    /// <summary>
    /// FOR %%I IN (*.TXT) DO ... expands wildcard to matching files.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_For_WildcardExpansion() {
        WithTempDirectory("dos_batch_for_wildcard", tempDir => {
            // Arrange — create three .TXT files and a .DAT file (shouldn't match)
            CreateTextFile(tempDir, "A.TXT", "aaa");
            CreateTextFile(tempDir, "B.TXT", "bbb");
            CreateTextFile(tempDir, "C.TXT", "ccc");
            CreateTextFile(tempDir, "SKIP.DAT", "nope");

            // Act — FOR iterates *.TXT, DEL each matching file
            RunBatchScript(tempDir, "FOR %%I IN (*.TXT) DO DEL %%I\r\n");

            // Assert — all .TXT files should be deleted, .DAT should remain
            File.Exists(Path.Join(tempDir, "A.TXT")).Should().BeFalse("FOR should have expanded *.TXT and deleted A.TXT");
            File.Exists(Path.Join(tempDir, "B.TXT")).Should().BeFalse("FOR should have expanded *.TXT and deleted B.TXT");
            File.Exists(Path.Join(tempDir, "C.TXT")).Should().BeFalse("FOR should have expanded *.TXT and deleted C.TXT");
            File.Exists(Path.Join(tempDir, "SKIP.DAT")).Should().BeTrue("SKIP.DAT should not match *.TXT");
        });
    }

    /// <summary>
    /// FOR %%I IN (*.TXT) DO ... with no matches executes nothing (no error).
    /// </summary>
    [Fact]
    public void HostRequestedBatch_For_WildcardNoMatches() {
        WithTempDirectory("dos_batch_for_wild_none", tempDir => {
            // Arrange — no .TXT files
            CreateTextFile(tempDir, "SKIP.DAT", "data");

            // Act
            RunBatchScript(tempDir, "FOR %%I IN (*.TXT) DO TYPE %%I >> OUT.TXT\r\nECHO DONE > DONE.TXT\r\n");

            // Assert — DONE marker should exist (batch continues) but OUT.TXT should not
            string done = File.ReadAllText(Path.Join(tempDir, "DONE.TXT")).Trim();
            done.Should().Be("DONE");
            File.Exists(Path.Join(tempDir, "OUT.TXT")).Should().BeFalse();
        });
    }

    // ───────────────────────── DIR command ─────────────────────────

    /// <summary>
    /// DIR lists files in the current directory including filenames and sizes.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Dir_ListsFiles() {
        WithTempDirectory("dos_batch_dir", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "HELLO.TXT", "Hello World");

            // Act
            RunBatchScript(tempDir, "DIR > OUT.TXT\r\n");

            // Assert — output should contain the filename
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).ToUpperInvariant();
            output.Should().Contain("HELLO");
            output.Should().Contain("TXT");
        });
    }

    /// <summary>
    /// DIR /B lists bare filenames only (one per line, no header/footer).
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Dir_BareFormat() {
        WithTempDirectory("dos_batch_dir_bare", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TXT", "a");
            CreateTextFile(tempDir, "B.DAT", "b");

            // Act
            RunBatchScript(tempDir, "DIR /B > OUT.TXT\r\n");

            // Assert — bare format: just filenames, one per line
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            // Should contain filenames (may include START.BAT + OUT.TXT depending on timing)
            lines.Should().Contain(l => l.Contains("A.TXT", StringComparison.OrdinalIgnoreCase));
            lines.Should().Contain(l => l.Contains("B.DAT", StringComparison.OrdinalIgnoreCase));
            lines.Count(l => string.Equals(l, "A.TXT", StringComparison.OrdinalIgnoreCase)).Should().Be(1,
                "DIR /B should not duplicate the first matching entry");
            lines.Count(l => string.Equals(l, "B.DAT", StringComparison.OrdinalIgnoreCase)).Should().Be(1,
                "DIR /B should not duplicate entries");
        });
    }

    /// <summary>
    /// DIR with a file pattern filters the listing.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Dir_WithPattern() {
        WithTempDirectory("dos_batch_dir_pattern", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TXT", "a");
            CreateTextFile(tempDir, "B.TXT", "b");
            CreateTextFile(tempDir, "C.DAT", "c");

            // Act
            RunBatchScript(tempDir, "DIR /B *.TXT > OUT.TXT\r\n");

            // Assert — only .TXT files
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.ToUpperInvariant().Should().Contain("A.TXT");
            output.ToUpperInvariant().Should().Contain("B.TXT");
            output.ToUpperInvariant().Should().NotContain("C.DAT");
        });
    }

    // ───────────────────────── COPY command ─────────────────────────

    /// <summary>
    /// COPY copies a single file to a new name.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Copy_SingleFile() {
        WithTempDirectory("dos_batch_copy", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "SRC.TXT", "hello copy");

            // Act
            RunBatchScript(tempDir, "COPY SRC.TXT DST.TXT\r\n");

            // Assert
            string content = File.ReadAllText(Path.Join(tempDir, "DST.TXT"));
            content.Should().Be("hello copy");
        });
    }

    /// <summary>
    /// COPY with wildcard copies matching files.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Copy_Wildcard() {
        WithTempDirectory("dos_batch_copy_wild", tempDir => {
            // Arrange
            string subDir = Path.Join(tempDir, "SUB");
            Directory.CreateDirectory(subDir);
            CreateTextFile(tempDir, "A.TXT", "aaa");
            CreateTextFile(tempDir, "B.TXT", "bbb");
            CreateTextFile(tempDir, "C.DAT", "ccc");

            // Act
            RunBatchScript(tempDir, "COPY *.TXT SUB > COPYLOG.LOG\r\n");

            // Assert — only .TXT files copied
            File.ReadAllText(Path.Join(subDir, "A.TXT")).Should().Be("aaa");
            File.ReadAllText(Path.Join(subDir, "B.TXT")).Should().Be("bbb");
            File.Exists(Path.Join(subDir, "C.DAT")).Should().BeFalse();
            string copyLog = File.ReadAllText(Path.Join(tempDir, "COPYLOG.LOG"));
            copyLog.Should().Contain("2 file(s) copied");
        });
    }

    [Fact]
    public void HostRequestedBatch_Copy_YAndVSwitches_AreAccepted() {
        WithTempDirectory("dos_batch_copy_switches", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "SRC.TXT", "copy switches");

            // Act
            RunBatchScript(tempDir, "COPY /Y /V SRC.TXT DST.TXT\r\n");

            // Assert
            File.ReadAllText(Path.Join(tempDir, "DST.TXT")).Should().Be("copy switches");
        });
    }

    /// <summary>
    /// COPY of a nonexistent file reports an error.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Copy_NonexistentSource() {
        WithTempDirectory("dos_batch_copy_missing", tempDir => {
            // Act
            RunBatchScript(tempDir, "COPY NOSRC.TXT DST.TXT > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty("COPY of missing file should produce an error message");
            File.Exists(Path.Join(tempDir, "DST.TXT")).Should().BeFalse();
        });
    }

    /// <summary>
    /// COPY single file to a directory should place the file inside that directory.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Copy_SingleFileToDirectory() {
        WithTempDirectory("dos_batch_copy_to_dir", tempDir => {
            // Arrange
            string subDir = Path.Join(tempDir, "DEST");
            Directory.CreateDirectory(subDir);
            CreateTextFile(tempDir, "SRC.TXT", "hello dir copy");

            // Act
            RunBatchScript(tempDir, "COPY SRC.TXT DEST\r\n");

            // Assert — file should be in DEST\SRC.TXT
            string destFile = Path.Join(subDir, "SRC.TXT");
            File.Exists(destFile).Should().BeTrue("COPY to a directory should place the file inside it");
            File.ReadAllText(destFile).Should().Be("hello dir copy");
        });
    }

    // ───────────────────────── DATE / TIME / VER / VOL commands ─────────────────────────

    /// <summary>
    /// DATE prints a date string to stdout.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Date_PrintsDate() {
        WithTempDirectory("dos_batch_date", tempDir => {
            // Act
            RunBatchScript(tempDir, "DATE /T > OUT.TXT\r\n");

            // Assert — should contain some date-like output
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty();
            // Should contain at least a year-like number
            output.Should().MatchRegex(@"\d{4}");
        });
    }

    [Fact]
    public void HostRequestedBatch_Date_SetValue_ChangesDisplayedDate() {
        WithTempDirectory("dos_batch_date_set", tempDir => {
            // Act
            RunBatchScript(tempDir, "DATE 02-29-2024\r\nDATE /T > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().Contain("2024");
            output.Should().Contain("02-29");
        });
    }

    [Fact]
    public void HostRequestedBatch_Date_HostSyncSwitch_ProducesDateOutput() {
        WithTempDirectory("dos_batch_date_host_sync", tempDir => {
            // Act
            RunBatchScript(tempDir, "DATE /H\r\nDATE /T > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().MatchRegex(@"\d{2}-\d{2}-\d{4}");
        });
    }

    /// <summary>
    /// TIME prints a time string to stdout.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Time_PrintsTime() {
        WithTempDirectory("dos_batch_time", tempDir => {
            // Act
            RunBatchScript(tempDir, "TIME /T > OUT.TXT\r\n");

            // Assert — should contain digits with colons (HH:MM:SS)
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty();
            output.Should().MatchRegex(@"\d+:\d+");
        });
    }

    [Fact]
    public void HostRequestedBatch_Time_SetValue_ChangesDisplayedTime() {
        WithTempDirectory("dos_batch_time_set", tempDir => {
            // Act
            RunBatchScript(tempDir, "TIME 13:14:15\r\nTIME /T > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().Contain("13:14:15");
        });
    }

    [Fact]
    public void HostRequestedBatch_Time_HostSyncSwitch_ProducesTimeOutput() {
        WithTempDirectory("dos_batch_time_host_sync", tempDir => {
            // Act
            RunBatchScript(tempDir, "TIME /H\r\nTIME /T > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().MatchRegex(@"\d{2}:\d{2}:\d{2}");
        });
    }

    /// <summary>
    /// VER prints a DOS version string.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Ver_PrintsVersion() {
        WithTempDirectory("dos_batch_ver", tempDir => {
            // Act
            RunBatchScript(tempDir, "VER > OUT.TXT\r\n");

            // Assert — should mention a version
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty();
        });
    }

    /// <summary>
    /// VOL prints the volume label for the current drive.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Vol_PrintsVolumeLabel() {
        WithTempDirectory("dos_batch_vol", tempDir => {
            // Act
            RunBatchScript(tempDir, "VOL > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Trim();
            output.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void HostRequestedBatch_Date_HelpSwitch_PrintsUsage() {
        WithTempDirectory("dos_batch_date_help", tempDir => {
            // Act
            RunBatchScript(tempDir, "DATE /? > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).ToUpperInvariant();
            output.Should().Contain("DATE");
        });
    }

    [Fact]
    public void HostRequestedBatch_Time_HelpSwitch_PrintsUsage() {
        WithTempDirectory("dos_batch_time_help", tempDir => {
            // Act
            RunBatchScript(tempDir, "TIME /? > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).ToUpperInvariant();
            output.Should().Contain("TIME");
        });
    }

    [Fact]
    public void HostRequestedBatch_Dir_Ad_ListsDirectoriesOnly() {
        WithTempDirectory("dos_batch_dir_ad", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "SUBDIR");
            CreateTextFile(tempDir, "FILE.TXT", "x");

            // Act
            RunBatchScript(tempDir, "DIR /B /AD > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).ToUpperInvariant();
            output.Should().Contain("SUBDIR");
            output.Should().NotContain("FILE.TXT");
        });
    }

    [Fact]
    public void HostRequestedBatch_Dir_AminusD_ListsFilesOnly() {
        WithTempDirectory("dos_batch_dir_a_minus_d", tempDir => {
            // Arrange
            CreateDirectoryPath(tempDir, "SUBDIR");
            CreateTextFile(tempDir, "FILE.TXT", "x");

            // Act
            RunBatchScript(tempDir, "DIR /B /A-D > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).ToUpperInvariant();
            output.Should().Contain("FILE.TXT");
            output.Should().NotContain("SUBDIR");
        });
    }

    [Fact]
    public void HostRequestedBatch_Dir_On_SortsByNameAscending() {
        WithTempDirectory("dos_batch_dir_sort_on", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "B.TXT", "b");
            CreateTextFile(tempDir, "A.TXT", "a");

            // Act
            RunBatchScript(tempDir, "DIR /B /ON *.TXT > OUT.TXT\r\n");

            // Assert
            string[] lines = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"))
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int aIndex = Array.FindIndex(lines, line => line.Equals("A.TXT", StringComparison.OrdinalIgnoreCase));
            int bIndex = Array.FindIndex(lines, line => line.Equals("B.TXT", StringComparison.OrdinalIgnoreCase));
            aIndex.Should().BeGreaterThanOrEqualTo(0);
            bIndex.Should().BeGreaterThanOrEqualTo(0);
            aIndex.Should().BeLessThan(bIndex);
        });
    }

    [Fact]
    public void HostRequestedBatch_Dir_OminusN_SortsByNameDescending() {
        WithTempDirectory("dos_batch_dir_sort_o_minus_n", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "B.TXT", "b");
            CreateTextFile(tempDir, "A.TXT", "a");

            // Act
            RunBatchScript(tempDir, "DIR /B /O-N *.TXT > OUT.TXT\r\n");

            // Assert
            string[] lines = File.ReadAllText(Path.Join(tempDir, "OUT.TXT"))
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int aIndex = Array.FindIndex(lines, line => line.Equals("A.TXT", StringComparison.OrdinalIgnoreCase));
            int bIndex = Array.FindIndex(lines, line => line.Equals("B.TXT", StringComparison.OrdinalIgnoreCase));
            aIndex.Should().BeGreaterThanOrEqualTo(0);
            bIndex.Should().BeGreaterThanOrEqualTo(0);
            bIndex.Should().BeLessThan(aIndex);
        });
    }

    [Fact]
    public void HostRequestedBatch_Set_PromptOption_PrintsUnsupportedMessage() {
        WithTempDirectory("dos_batch_set_prompt_unsupported", tempDir => {
            // Act
            RunBatchScript(tempDir, "SET /P MYVAR=QUESTION? > OUT.TXT\r\n");

            // Assert
            string output = File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).ToUpperInvariant();
            output.Should().Contain("UNSUPPORTED");
        });
    }

    /// <summary>
    /// Bare * wildcard (no dot, no extension) should match all files, same as *.*
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ForBareStarMatchesAllFiles() {
        WithTempDirectory("dos_batch_for_bare_star", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TXT", "aaa");
            CreateTextFile(tempDir, "B.DAT", "bbb");

            // Act — bare * should match BOTH files regardless of extension
            RunBatchScript(tempDir, "FOR %%I IN (*) DO TYPE %%I >> OUT.TXT\r\n");

            // Assert
            string outPath = Path.Join(tempDir, "OUT.TXT");
            File.Exists(outPath).Should().BeTrue("FOR with bare * should match files");
            string content = File.ReadAllText(outPath);
            content.Should().Contain("aaa", "bare * must match .TXT files");
            content.Should().Contain("bbb", "bare * must match .DAT files");
        });
    }

    /// <summary>
    /// External .BAT command found via PATH environment variable should be executed.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_PathSearchFindsExternalBat() {
        WithTempDirectory("dos_batch_path_search", tempDir => {
            // Arrange — put TOOL.BAT in a subdirectory and add that dir to PATH
            string toolDir = Path.Join(tempDir, "TOOLS");
            Directory.CreateDirectory(toolDir);
            CreateTextFile(toolDir, "TOOL.BAT", "ECHO FOUND > RESULT.TXT\r\n");

            // Act — set PATH and invoke TOOL without full path
            RunBatchScript(tempDir, "PATH C:\\TOOLS\r\nTOOL\r\n");

            // Assert — RESULT.TXT should be created (in CWD = tempDir)
            string resultPath = Path.Join(tempDir, "RESULT.TXT");
            File.Exists(resultPath).Should().BeTrue(
                "TOOL.BAT in PATH directory should be found and executed");
        });
    }

    // ───────────────────────── MOVE command ─────────────────────────

    /// <summary>
    /// MOVE renames a single file in the same directory.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Move_RenamesFileInSameDirectory() {
        WithTempDirectory("dos_batch_move_rename", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "SRC.TXT", "hello");

            // Act
            RunBatchScript(tempDir, "MOVE SRC.TXT DST.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "SRC.TXT")).Should().BeFalse(
                "MOVE should remove the source file");
            File.ReadAllText(Path.Join(tempDir, "DST.TXT")).Should().Be("hello",
                "MOVE should create destination with source content");
        });
    }

    /// <summary>
    /// MOVE moves a file into a subdirectory when the destination is a directory.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Move_FileIntoSubdirectory() {
        WithTempDirectory("dos_batch_move_to_dir", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "DATA.TXT", "payload");
            string subDir = Path.Join(tempDir, "SUB");
            Directory.CreateDirectory(subDir);

            // Act
            RunBatchScript(tempDir, "MOVE DATA.TXT SUB\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "DATA.TXT")).Should().BeFalse(
                "MOVE should remove the source file");
            File.ReadAllText(Path.Join(subDir, "DATA.TXT")).Should().Be("payload",
                "MOVE should place the file in the target directory");
        });
    }

    /// <summary>
    /// MOVE with wildcard moves all matching files into a directory.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Move_WildcardIntoDirectory() {
        WithTempDirectory("dos_batch_move_wild", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TXT", "aaa");
            CreateTextFile(tempDir, "B.TXT", "bbb");
            CreateTextFile(tempDir, "KEEP.DAT", "keep");
            string dest = Path.Join(tempDir, "DEST");
            Directory.CreateDirectory(dest);

            // Act
            RunBatchScript(tempDir, "MOVE *.TXT DEST\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "A.TXT")).Should().BeFalse("source A.TXT should be removed");
            File.Exists(Path.Join(tempDir, "B.TXT")).Should().BeFalse("source B.TXT should be removed");
            File.Exists(Path.Join(tempDir, "KEEP.DAT")).Should().BeTrue("non-matching file should remain");
            File.ReadAllText(Path.Join(dest, "A.TXT")).Should().Be("aaa");
            File.ReadAllText(Path.Join(dest, "B.TXT")).Should().Be("bbb");
        });
    }

    /// <summary>
    /// MOVE on nonexistent file prints an error and does not crash.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Move_NonexistentSource() {
        WithTempDirectory("dos_batch_move_missing", tempDir => {
            // Act — should not throw
            RunBatchScript(tempDir, "MOVE NOPE.TXT DEST.TXT\r\nECHO OK > DONE.TXT\r\n");

            // Assert — batch continues after the error
            File.Exists(Path.Join(tempDir, "DONE.TXT")).Should().BeTrue(
                "batch should continue after MOVE fails on a missing file");
        });
    }

    // ───────────────────────── COPY concat mode ─────────────────────────

    /// <summary>
    /// COPY A+B C concatenates two files into a single destination.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Copy_ConcatTwoFiles() {
        WithTempDirectory("dos_batch_copy_concat2", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "A.TXT", "Hello");
            CreateTextFile(tempDir, "B.TXT", "World");

            // Act
            RunBatchScript(tempDir, "COPY A.TXT+B.TXT COMBO.TXT\r\n");

            // Assert
            File.ReadAllText(Path.Join(tempDir, "COMBO.TXT")).Should().Be("HelloWorld",
                "COPY with + should concatenate file contents");
        });
    }

    /// <summary>
    /// COPY A+B+C D concatenates three files into a single destination.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Copy_ConcatThreeFiles() {
        WithTempDirectory("dos_batch_copy_concat3", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "X.TXT", "One");
            CreateTextFile(tempDir, "Y.TXT", "Two");
            CreateTextFile(tempDir, "Z.TXT", "Three");

            // Act
            RunBatchScript(tempDir, "COPY X.TXT+Y.TXT+Z.TXT ALL.TXT\r\n");

            // Assert
            File.ReadAllText(Path.Join(tempDir, "ALL.TXT")).Should().Be("OneTwoThree",
                "COPY with + should concatenate all source files in order");
        });
    }

    // ─────────────── Path resolution: case-insensitive create ───────────────

    /// <summary>
    /// Creating a file in a subdirectory whose name has different case on disk
    /// must still succeed. The DOS path uses uppercase "SUB" but the host
    /// directory was created as "Sub" — CreateFileUsingHandle must resolve the
    /// parent directory case-insensitively before calling File.Create.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Redirect_CreateFileInCaseMismatchedSubdirectory() {
        WithTempDirectory("dos_batch_case_subdir", tempDir => {
            // Arrange — create host directory with mixed case "Sub"
            string subDir = Path.Join(tempDir, "Sub");
            Directory.CreateDirectory(subDir);

            // Act — DOS path references "SUB" (uppercase) via redirect
            RunBatchScript(tempDir, "ECHO hello > SUB\\OUT.TXT\r\n");

            // Assert — file must be created despite case mismatch
            string outPath = Path.Join(subDir, "OUT.TXT");
            File.Exists(outPath).Should().BeTrue(
                "CreateFileUsingHandle must resolve directory case-insensitively");
            File.ReadAllText(outPath).Trim().Should().Be("hello");
        });
    }

    // ───────────────────────── DELETE alias ─────────────────────────

    /// <summary>
    /// DELETE is a DOSBox-registered alias for DEL. It must be recognized.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_Delete_IsAliasForDel() {
        WithTempDirectory("dos_batch_delete_alias", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "VICTIM.TXT", "bye");

            // Act
            RunBatchScript(tempDir, "DELETE VICTIM.TXT\r\n");

            // Assert
            File.Exists(Path.Join(tempDir, "VICTIM.TXT")).Should().BeFalse(
                "DELETE should work as an alias for DEL");
        });
    }

    // ───────────────────────── Jill of the Jungle batch ─────────────────────────

    /// <summary>
    /// Simulates jillep.bat from "Jill of the Jungle: The Complete Trilogy" (GOG edition).
    /// The batch displays a menu via ECHO, uses CHOICE /c1234 /s ... /n (no colon, switches mixed
    /// with prompt text, /n at end), then dispatches to labeled sections that launch EXE stubs.
    /// Pressing "1" should GOTO :101, run JILL1.EXE (stub writes '1' to video), then EXIT.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_JillOfTheJungle_ChoiceDispatchesToExeStub() {
        WithTempDirectory("dos_batch_jill", tempDir => {
            // Arrange: EXE stubs that write a character to video memory to prove they ran
            CreateBinaryFile(tempDir, "JILL1.EXE", BuildVideoWriterCom('1', 0));
            CreateBinaryFile(tempDir, "JILL2.EXE", BuildVideoWriterCom('2', 0));
            CreateBinaryFile(tempDir, "JILL3.EXE", BuildVideoWriterCom('3', 0));

            // Input "3" via redirected stdin so CHOICE picks option 3 → ERRORLEVEL 3
            CreateTextFile(tempDir, "KEY.TXT", "3");

            // The actual jillep.bat content (simplified to use input redirection for testing)
            string jillBat =
                "ECHO Menu\r\n" +
                "choice /c1234 /s Which game do you want to run? [1-4]: /n < KEY.TXT\r\n" +
                "if errorlevel 4 goto exit\r\n" +
                "if errorlevel 3 goto 301\r\n" +
                "if errorlevel 2 goto 201\r\n" +
                "if errorlevel 1 goto 101\r\n" +
                "\r\n" +
                ":101\r\n" +
                "@ECHO off\r\n" +
                "cls\r\n" +
                "JILL1.EXE\r\n" +
                "exit\r\n" +
                "\r\n" +
                ":201\r\n" +
                "@ECHO off\r\n" +
                "cls\r\n" +
                "JILL2.EXE\r\n" +
                "exit\r\n" +
                "\r\n" +
                ":301\r\n" +
                "@ECHO off\r\n" +
                "cls\r\n" +
                "JILL3.EXE\r\n" +
                "exit\r\n" +
                "\r\n" +
                ":exit\r\n" +
                "exit\r\n";

            // Act
            char actual = RunAndCaptureVideoCell(
                CreateTextFile(tempDir, "JILLEP.BAT", jillBat), tempDir);

            // Assert: option "3" → goto :301 → JILL3.EXE → writes '3' to video
            actual.Should().Be('3');
        });
    }

    /// <summary>
    /// Validates that CHOICE /c without colon (e.g. /c1234 instead of /C:1234) is parsed correctly,
    /// as used by many real-world DOS batch files including Jill of the Jungle.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ChoiceWithoutColon_ParsesChoiceKeys() {
        WithTempDirectory("dos_batch_choice_no_colon", tempDir => {
            // Arrange: CHOICE /c123 with '2' → ERRORLEVEL=2
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('2', 0));
            CreateTextFile(tempDir, "KEY.TXT", "2");

            // Act & Assert
            RunAndAssertVideoCellFromScript(tempDir,
                "CHOICE /c123 < KEY.TXT\r\nIF ERRORLEVEL 2 CALL W2.COM\r\n", '2');
        });
    }

    /// <summary>
    /// Validates that CHOICE /n can appear after prompt text (as in jillep.bat),
    /// and still suppresses the default [1,2,3,4]? prompt.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ChoiceNFlagAfterText_SuppressesDefaultPrompt() {
        WithTempDirectory("dos_batch_choice_n_after_text", tempDir => {
            // Arrange
            CreateTextFile(tempDir, "KEY.TXT", "Y");

            // Act: /n appears after prompt text
            RunBatchScript(tempDir,
                "CHOICE /C:YN Pick one /n > PROMPT.TXT < KEY.TXT\r\n");

            // Assert: /n should suppress the [Y,N]? suffix
            string prompt = File.ReadAllText(Path.Join(tempDir, "PROMPT.TXT"));
            prompt.Should().NotContain("[Y,N]?");
            prompt.Should().Contain("Pick one");
        });
    }

    /// <summary>
    /// CHOICE with preloaded keyboard input (no stdin redirection) reads a key via the emulation loop
    /// and sets ERRORLEVEL correctly. This validates that CHOICE does not stall the UI by blocking
    /// outside the emulation loop — the COM stub runs inside the loop where keyboard IRQs flow.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_ChoiceWithPreloadedKey_SetsErrorlevel() {
        WithTempDirectory("dos_batch_choice_key", tempDir => {
            // Arrange: EXE stubs that write a character to video memory
            CreateBinaryFile(tempDir, "W1.COM", BuildVideoWriterCom('1', 0));
            CreateBinaryFile(tempDir, "W2.COM", BuildVideoWriterCom('2', 0));

            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "CHOICE /C:AB\r\n" +
                "IF ERRORLEVEL 2 W2.COM\r\n" +
                "IF ERRORLEVEL 1 W1.COM\r\n");

            // Pre-load 'B' key (scan=0x30, ascii=0x42) — should be ERRORLEVEL 2
            ushort[] keys = new ushort[] { 0x3042 };

            // Act
            char[] cells = RunWithPreloadedKeysAndCaptureVideoCells(startBatchPath, tempDir, 1, keys);

            // Assert: 'B' → ERRORLEVEL 2 → W2.COM writes '2'
            cells[0].Should().Be('2');
        });
    }

    /// <summary>
    /// PAUSE with preloaded keyboard input reads a key via the emulation loop and continues
    /// batch execution without stalling. This validates PAUSE runs as an in-memory COM stub.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_PauseWithPreloadedKey_ContinuesExecution() {
        WithTempDirectory("dos_batch_pause_key", tempDir => {
            // Arrange: COM stub that writes to video
            CreateBinaryFile(tempDir, "MARK.COM", BuildVideoWriterCom('P', 0));

            string startBatchPath = CreateTextFile(tempDir, "START.BAT",
                "PAUSE\r\n" +
                "MARK.COM\r\n");

            // Pre-load any key: space (scan=0x39, ascii=0x20)
            ushort[] keys = new ushort[] { 0x3920 };

            // Act
            char[] cells = RunWithPreloadedKeysAndCaptureVideoCells(startBatchPath, tempDir, 1, keys);

            // Assert: PAUSE consumed the key and batch continued → MARK.COM writes 'P'
            cells[0].Should().Be('P');
        });
    }
}
