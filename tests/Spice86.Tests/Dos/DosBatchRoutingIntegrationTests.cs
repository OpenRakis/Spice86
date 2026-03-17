namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Shared.Utils;

using System;
using System.IO;

using Xunit;

public class DosBatchRoutingIntegrationTests {
    [Fact]
    public void HostRequestedCom_StillExecutesThroughBatchRoutingPipeline() {
        string tempDir = CreateTempDirectory("dos_host_com");

        try {
            string writerComPath = Path.Combine(tempDir, "WRITER.COM");
            File.WriteAllBytes(writerComPath, BuildVideoWriterCom('C', 0));

            RunAndAssertVideoCell(writerComPath, tempDir, 'C');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_UsesInternalCallAndLaunchesComProgram() {
        string tempDir = CreateTempDirectory("dos_batch_call");

        try {
            string writerComPath = Path.Combine(tempDir, "WRITER.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(writerComPath, BuildVideoWriterCom('A', 0));
            File.WriteAllText(startBatchPath, "CALL WRITER.COM\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'A');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_CallWithoutExtension_PrefersBatOverCom() {
        string tempDir = CreateTempDirectory("dos_batch_call_noext_bat_first");

        try {
            string toolBatchPath = Path.Combine(tempDir, "TOOL.BAT");
            string toolComPath = Path.Combine(tempDir, "TOOL.COM");
            string batchWinnerComPath = Path.Combine(tempDir, "BATWIN.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllText(toolBatchPath, "CALL BATWIN.COM\r\n");
            File.WriteAllBytes(toolComPath, BuildVideoWriterCom('C', 0));
            File.WriteAllBytes(batchWinnerComPath, BuildVideoWriterCom('B', 0));
            File.WriteAllText(startBatchPath, "CALL TOOL\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'B');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_CallWithoutExtension_FallsBackToComThenExe() {
        string tempDir = CreateTempDirectory("dos_batch_call_noext_com_then_exe");

        try {
            string toolComPath = Path.Combine(tempDir, "TOOL.COM");
            string toolExePath = Path.Combine(tempDir, "TOOL.EXE");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(toolComPath, BuildVideoWriterCom('C', 0));
            File.WriteAllBytes(toolExePath, BuildVideoWriterCom('E', 0));
            File.WriteAllText(startBatchPath, "CALL TOOL\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'C');

            File.Delete(toolComPath);
            RunAndAssertVideoCell(startBatchPath, tempDir, 'E');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_MaupitiTatouBat_RunsFromNestedJeuxDirectory() {
        string tempDir = CreateTempDirectory("dos_batch_maupiti_tatou");

        try {
            string maupitiDirectoryPath = Path.Combine(tempDir, "JEUX", "MAUPITI");
            Directory.CreateDirectory(maupitiDirectoryPath);

            string tatouBatchPath = Path.Combine(maupitiDirectoryPath, "TATOU.BAT");
            string writerComPath = Path.Combine(maupitiDirectoryPath, "MAUPITIW.COM");
            string outputPath = Path.Combine(maupitiDirectoryPath, "MAUPITI.TXT");

            File.WriteAllBytes(writerComPath, BuildStdoutWriterCom("M"));
            File.WriteAllText(tatouBatchPath, "CALL MAUPITIW.COM > C:\\JEUX\\MAUPITI\\MAUPITI.TXT\r\n");

            RunWithoutVideoReadNoPit(tatouBatchPath, tempDir);

            string output = File.ReadAllText(outputPath);
            output.Should().Be("M");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_AitdTatouWithoutExtension_PrefersBatOverComAndExe() {
        string tempDir = CreateTempDirectory("dos_batch_aitd_tatou_noext");

        try {
            string aitdDirectoryPath = Path.Combine(tempDir, "JEUX", "AITD");
            Directory.CreateDirectory(aitdDirectoryPath);

            string tatouBatchPath = Path.Combine(aitdDirectoryPath, "TATOU.BAT");
            string tatouComPath = Path.Combine(aitdDirectoryPath, "TATOU.COM");
            string tatouExePath = Path.Combine(aitdDirectoryPath, "TATOU.EXE");
            string outputPath = Path.Combine(aitdDirectoryPath, "WINNER.TXT");
            string rootOutputPath = Path.Combine(tempDir, "WINNER.TXT");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllText(tatouBatchPath, "ECHO BAT> C:\\JEUX\\AITD\\WINNER.TXT\r\n");
            File.WriteAllBytes(tatouComPath, BuildStdoutWriterCom("C"));
            File.WriteAllBytes(tatouExePath, BuildStdoutWriterCom("E"));
            File.WriteAllText(startBatchPath, "CALL c:\\jeux\\aitd\\tatou\r\n");

            RunWithoutVideoReadNoPit(startBatchPath, tempDir);

            string? resolvedOutputPath = File.Exists(outputPath)
                ? outputPath
                : File.Exists(rootOutputPath)
                    ? rootOutputPath
                    : null;
            resolvedOutputPath.Should().NotBeNull();
            if (resolvedOutputPath == null) {
                throw new FileNotFoundException("WINNER.TXT was not created in expected locations.");
            }

            string output = File.ReadAllText(resolvedOutputPath);
            output.Should().Be("BAT\r\n");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_CanCallNestedBatch() {
        string tempDir = CreateTempDirectory("dos_nested_batch");

        try {
            string writerComPath = Path.Combine(tempDir, "WRITER.COM");
            string childBatchPath = Path.Combine(tempDir, "CHILD.BAT");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(writerComPath, BuildVideoWriterCom('B', 0));
            File.WriteAllText(childBatchPath, "CALL WRITER.COM\r\n");
            File.WriteAllText(startBatchPath, "CALL CHILD.BAT\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'B');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_GotoSkipsCommands() {
        string tempDir = CreateTempDirectory("dos_batch_goto");

        try {
            string skipComPath = Path.Combine(tempDir, "SKIP.COM");
            string passComPath = Path.Combine(tempDir, "PASS.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(skipComPath, BuildVideoWriterCom('S', 2));
            File.WriteAllBytes(passComPath, BuildVideoWriterCom('G', 0));
            File.WriteAllText(startBatchPath, "GOTO RUN\r\nCALL SKIP.COM\r\n:RUN\r\nCALL PASS.COM\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().Be('G');
            cells[1].Should().NotBe('S');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_IfErrorLevelDispatchesCommand() {
        string tempDir = CreateTempDirectory("dos_batch_if_errorlevel");

        try {
            string retComPath = Path.Combine(tempDir, "RET5.COM");
            string writerComPath = Path.Combine(tempDir, "W5.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(retComPath, BuildExitCodeCom(5));
            File.WriteAllBytes(writerComPath, BuildVideoWriterCom('E', 0));
            File.WriteAllText(startBatchPath, "CALL RET5.COM\r\nIF ERRORLEVEL 5 CALL W5.COM\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'E');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_IfNotErrorLevelSkipsCommand() {
        string tempDir = CreateTempDirectory("dos_batch_if_not_errorlevel");

        try {
            string retComPath = Path.Combine(tempDir, "RET5.COM");
            string skipComPath = Path.Combine(tempDir, "SKIP.COM");
            string passComPath = Path.Combine(tempDir, "PASS.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(retComPath, BuildExitCodeCom(5));
            File.WriteAllBytes(skipComPath, BuildVideoWriterCom('S', 0));
            File.WriteAllBytes(passComPath, BuildVideoWriterCom('P', 2));
            File.WriteAllText(startBatchPath,
                "CALL RET5.COM\r\nIF NOT ERRORLEVEL 5 CALL SKIP.COM\r\nIF ERRORLEVEL 5 CALL PASS.COM\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().NotBe('S');
            cells[1].Should().Be('P');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_IfNotStringComparisonExecutesExpectedBranch() {
        string tempDir = CreateTempDirectory("dos_batch_if_not_string");

        try {
            string skipComPath = Path.Combine(tempDir, "SKIP.COM");
            string passComPath = Path.Combine(tempDir, "PASS.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(skipComPath, BuildVideoWriterCom('S', 0));
            File.WriteAllBytes(passComPath, BuildVideoWriterCom('N', 2));
            File.WriteAllText(startBatchPath,
                "IF NOT \"ONE\"==\"ONE\" CALL SKIP.COM\r\nIF NOT \"ONE\"==\"TWO\" CALL PASS.COM\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().NotBe('S');
            cells[1].Should().Be('N');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_IfExistAndIfNotExistUseCorrectBranch() {
        string tempDir = CreateTempDirectory("dos_batch_if_exist");

        try {
            string existsComPath = Path.Combine(tempDir, "EXISTS.COM");
            string missingComPath = Path.Combine(tempDir, "MISSING.COM");
            string markerPath = Path.Combine(tempDir, "FLAG.TXT");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(existsComPath, BuildVideoWriterCom('E', 0));
            File.WriteAllBytes(missingComPath, BuildVideoWriterCom('M', 2));
            File.WriteAllText(markerPath, "1");
            File.WriteAllText(startBatchPath,
                "IF EXIST FLAG.TXT CALL EXISTS.COM\r\nIF NOT EXIST FLAG.TXT CALL MISSING.COM\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().Be('E');
            cells[1].Should().NotBe('M');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_ShiftUpdatesArguments() {
        string tempDir = CreateTempDirectory("dos_batch_shift");

        try {
            string w1ComPath = Path.Combine(tempDir, "W1.COM");
            string w2ComPath = Path.Combine(tempDir, "W2.COM");
            string routeBatchPath = Path.Combine(tempDir, "ROUTE.BAT");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(w1ComPath, BuildVideoWriterCom('1', 0));
            File.WriteAllBytes(w2ComPath, BuildVideoWriterCom('2', 2));
            File.WriteAllText(routeBatchPath,
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nSHIFT\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\n");
            File.WriteAllText(startBatchPath, "CALL ROUTE.BAT ONE TWO\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().Be('1');
            cells[1].Should().Be('2');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_ForIteratesAndCallsBatch() {
        string tempDir = CreateTempDirectory("dos_batch_for");

        try {
            string w1ComPath = Path.Combine(tempDir, "W1.COM");
            string w2ComPath = Path.Combine(tempDir, "W2.COM");
            string routeBatchPath = Path.Combine(tempDir, "ROUTE.BAT");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(w1ComPath, BuildVideoWriterCom('O', 0));
            File.WriteAllBytes(w2ComPath, BuildVideoWriterCom('T', 2));
            File.WriteAllText(routeBatchPath,
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\n");
            File.WriteAllText(startBatchPath, "FOR %%I IN (ONE TWO) DO CALL ROUTE.BAT %%I\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().Be('O');
            cells[1].Should().Be('T');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_ForSupportsCommaAndSemicolonDelimiters() {
        string tempDir = CreateTempDirectory("dos_batch_for_delimiters");

        try {
            string w1ComPath = Path.Combine(tempDir, "W1.COM");
            string w2ComPath = Path.Combine(tempDir, "W2.COM");
            string w3ComPath = Path.Combine(tempDir, "W3.COM");
            string routeBatchPath = Path.Combine(tempDir, "ROUTE.BAT");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(w1ComPath, BuildVideoWriterCom('A', 0));
            File.WriteAllBytes(w2ComPath, BuildVideoWriterCom('B', 2));
            File.WriteAllBytes(w3ComPath, BuildVideoWriterCom('C', 4));
            File.WriteAllText(routeBatchPath,
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\nIF \"%1\"==\"THREE\" CALL W3.COM\r\n");
            File.WriteAllText(startBatchPath, "FOR %%I IN (ONE,TWO;THREE) DO CALL ROUTE.BAT %%I\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 3);
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
            cells[2].Should().Be('C');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_OutputRedirectionWritesToFile() {
        string tempDir = CreateTempDirectory("dos_batch_redirection");

        try {
            string writerComPath = Path.Combine(tempDir, "OUT.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");
            string outputPath = Path.Combine(tempDir, "OUT.TXT");

            File.WriteAllBytes(writerComPath, BuildStdoutWriterCom("OK"));
            File.WriteAllText(startBatchPath, "CALL OUT.COM > OUT.TXT\r\n");

            RunWithoutVideoRead(startBatchPath, tempDir);

            string output = File.ReadAllText(outputPath);
            output.Should().Be("OK");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Theory]
    [InlineData("ECHO TEST>OUT.TXT", "TEST\r\n")]
    [InlineData("ECHO TEST >OUT.TXT", "TEST \r\n")]
    [InlineData("ECHO TEST> OUT.TXT", "TEST\r\n")]
    [InlineData("ECHO TEST > OUT.TXT", "TEST \r\n")]
    [InlineData("ECHO TEST>OUT.TXT  ", "TEST  \r\n")]
    [InlineData("ECHO TEST > OUT.TXT ", "TEST  \r\n")]
    public void HostRequestedBatch_OutputRedirectionPreservesEchoSpacing(string batchLine, string expectedOutput) {
        string tempDir = CreateTempDirectory("dos_batch_redirection_spacing");

        try {
            RunBatchScript(tempDir, batchLine + "\r\n");

            string output = File.ReadAllText(Path.Combine(tempDir, "OUT.TXT"));
            output.Should().Be(expectedOutput);
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_AppendRedirectionAppendsToFile() {
        string tempDir = CreateTempDirectory("dos_batch_append_redirection");

        try {
            string firstComPath = Path.Combine(tempDir, "FIRST.COM");
            string secondComPath = Path.Combine(tempDir, "SECOND.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");
            string outputPath = Path.Combine(tempDir, "OUT.TXT");

            File.WriteAllBytes(firstComPath, BuildStdoutWriterCom("AA"));
            File.WriteAllBytes(secondComPath, BuildStdoutWriterCom("BB"));
            File.WriteAllText(startBatchPath, "CALL FIRST.COM > OUT.TXT\r\nCALL SECOND.COM >> OUT.TXT\r\n");

            RunWithoutVideoRead(startBatchPath, tempDir);

            string output = File.ReadAllText(outputPath);
            output.Should().Be("AABB");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Theory]
    [InlineData("CALL PROD.COM | CALL CONS.COM")]
    [InlineData("CALL PROD.COM|CALL CONS.COM")]
    [InlineData("CALL PROD.COM| CALL CONS.COM")]
    public void HostRequestedBatch_PipeTransfersStdoutToStdin(string batchLine) {
        string tempDir = CreateTempDirectory("dos_batch_pipe");

        try {
            string producerComPath = Path.Combine(tempDir, "PROD.COM");
            string consumerComPath = Path.Combine(tempDir, "CONS.COM");

            File.WriteAllBytes(producerComPath, BuildStdoutWriterCom("P"));
            File.WriteAllBytes(consumerComPath, BuildStdinToVideoWriterCom(0));

            RunAndAssertVideoCellFromScript(tempDir, batchLine + "\r\n", 'P');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_PipelineSupportsInputRedirectionOnFirstSegment() {
        string tempDir = CreateTempDirectory("dos_batch_pipe_input_first");

        try {
            string passComPath = Path.Combine(tempDir, "PASS.COM");
            string consumerComPath = Path.Combine(tempDir, "CONS.COM");
            string inputPath = Path.Combine(tempDir, "IN.TXT");

            File.WriteAllBytes(passComPath, BuildStdinToStdoutCom());
            File.WriteAllBytes(consumerComPath, BuildStdinToVideoWriterCom(0));
            File.WriteAllText(inputPath, "Q");

            RunAndAssertVideoCellFromScript(tempDir, "CALL PASS.COM < IN.TXT | CALL CONS.COM\r\n", 'Q');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_InputRedirectionFeedsStdIn() {
        string tempDir = CreateTempDirectory("dos_batch_input_redirection");

        try {
            string consumerComPath = Path.Combine(tempDir, "READ.COM");
            string inputPath = Path.Combine(tempDir, "IN.TXT");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(consumerComPath, BuildStdinToVideoWriterCom(0));
            File.WriteAllText(inputPath, "R");
            File.WriteAllText(startBatchPath, "CALL READ.COM < IN.TXT\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'R');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_SetUpdatesDosEnvironmentForExpansion() {
        string tempDir = CreateTempDirectory("dos_batch_set_env");

        try {
            string writerComPath = Path.Combine(tempDir, "WENV.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(writerComPath, BuildVideoWriterCom('V', 0));
            File.WriteAllText(startBatchPath,
                "SET SPICE86_BATCH_VAR=YES\r\nIF \"%SPICE86_BATCH_VAR%\"==\"YES\" CALL WENV.COM\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'V');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_SetEmptyAssignmentClearsEnvironmentVariable() {
        string tempDir = CreateTempDirectory("dos_batch_set_clear_env");

        try {
            string writerComPath = Path.Combine(tempDir, "WCLEAR.COM");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");

            File.WriteAllBytes(writerComPath, BuildVideoWriterCom('C', 0));
            File.WriteAllText(startBatchPath,
                "SET SPICE86_BATCH_VAR=YES\r\nSET SPICE86_BATCH_VAR=\r\nIF \"%SPICE86_BATCH_VAR%\"==\"\" CALL WCLEAR.COM\r\n");

            RunAndAssertVideoCell(startBatchPath, tempDir, 'C');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_EchoWritesToRedirectedFile() {
        string tempDir = CreateTempDirectory("dos_batch_echo_redirect");

        try {
            string startBatchPath = Path.Combine(tempDir, "START.BAT");
            string outputPath = Path.Combine(tempDir, "OUT.TXT");

            File.WriteAllText(startBatchPath, "ECHO HELLO > OUT.TXT\r\n");

            RunWithoutVideoRead(startBatchPath, tempDir);

            string output = File.ReadAllText(outputPath);
            output.Should().Be("HELLO \r\n");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_InvalidRedirectionSyntaxDoesNotExecuteCommand() {
        string tempDir = CreateTempDirectory("dos_batch_invalid_redirect");

        try {
            string startBatchPath = Path.Combine(tempDir, "START.BAT");
            string outputPath = Path.Combine(tempDir, "OUT.TXT");

            File.WriteAllText(startBatchPath, "ECHO BAD > > OUT.TXT\r\n");

            RunWithoutVideoRead(startBatchPath, tempDir);

            File.Exists(outputPath).Should().BeFalse();
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
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
        string tempDir = CreateTempDirectory("dos_batch_invalid_redirect_matrix");

        try {
            RunBatchScript(tempDir, batchLine + "\r\n");
            File.Exists(Path.Combine(tempDir, "OUT.TXT")).Should().BeFalse();
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Theory]
    [InlineData("ECHO VALUE>OUT1.TXT>OUT2.TXT", "VALUE\r\n")]
    [InlineData("ECHO VALUE>    OUT1.TXT>     OUT2.TXT", "VALUE\r\n")]
    [InlineData("ECHO VALUE>OUT1.TXT  >OUT2.TXT", "VALUE  \r\n")]
    public void HostRequestedBatch_MultipleOutputRedirections_LastOneWins(string batchLine, string expectedOutput) {
        string tempDir = CreateTempDirectory("dos_batch_multi_output");

        try {
            RunBatchScript(tempDir, batchLine + "\r\n");

            string out2 = File.ReadAllText(Path.Combine(tempDir, "OUT2.TXT"));
            out2.Should().Be(expectedOutput);
            File.Exists(Path.Combine(tempDir, "OUT1.TXT")).Should().BeFalse();
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Theory]
    [InlineData("CALL READ.COM < IN1.TXT < IN2.TXT")]
    [InlineData("CALL READ.COM<IN1.TXT<IN2.TXT")]
    public void HostRequestedBatch_MultipleInputRedirections_LastOneWins(string batchLine) {
        string tempDir = CreateTempDirectory("dos_batch_multi_input");

        try {
            File.WriteAllBytes(Path.Combine(tempDir, "READ.COM"), BuildStdinToVideoWriterCom(0));
            File.WriteAllText(Path.Combine(tempDir, "IN1.TXT"), "A");
            File.WriteAllText(Path.Combine(tempDir, "IN2.TXT"), "B");

            RunAndAssertVideoCellFromScript(tempDir, batchLine + "\r\n", 'B');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_DoubleInputOperator_LastOneWins() {
        string tempDir = CreateTempDirectory("dos_batch_double_input_operator");

        try {
            File.WriteAllBytes(Path.Combine(tempDir, "READ.COM"), BuildStdinToVideoWriterCom(0));
            File.WriteAllText(Path.Combine(tempDir, "IN1.TXT"), "X");
            File.WriteAllText(Path.Combine(tempDir, "IN2.TXT"), "Y");

            RunAndAssertVideoCellFromScript(tempDir, "CALL READ.COM << IN1.TXT << IN2.TXT\r\n", 'Y');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Theory]
    [InlineData("CALL WRITE.COM | > OUT.TXT")]
    [InlineData("CALL WRITE.COM|>OUT.TXT")]
    [InlineData("CALL WRITE.COM |< IN.TXT")]
    [InlineData("CALL WRITE.COM| < IN.TXT")]
    [InlineData("CALL WRITE.COM < > IN.TXT")]
    public void HostRequestedBatch_InvalidRedirectionSyntaxCases_DoNotLaunchExternalCommand(string batchLine) {
        string tempDir = CreateTempDirectory("dos_batch_invalid_redirect_launch_matrix");

        try {
            File.WriteAllBytes(Path.Combine(tempDir, "WRITE.COM"), BuildVideoWriterCom('Z', 0));
            File.WriteAllText(Path.Combine(tempDir, "IN.TXT"), "I");

            RunAndAssertVideoCellNotWrittenFromScript(tempDir, batchLine + "\r\n", 'Z');
            File.Exists(Path.Combine(tempDir, "OUT.TXT")).Should().BeFalse();
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_EchoOffSetsStatusOutput() {
        string tempDir = CreateTempDirectory("dos_batch_echo_off_status");

        try {
            RunBatchScript(tempDir, "ECHO OFF\r\nECHO > STATUS.TXT\r\n");

            string status = File.ReadAllText(Path.Combine(tempDir, "STATUS.TXT"));
            status.Should().Be("ECHO is OFF.\r\n");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_EchoOnAfterOffRestoresStatusOutput() {
        string tempDir = CreateTempDirectory("dos_batch_echo_on_status");

        try {
            RunBatchScript(tempDir, "ECHO OFF\r\nECHO ON\r\nECHO > STATUS.TXT\r\n");

            string status = File.ReadAllText(Path.Combine(tempDir, "STATUS.TXT"));
            status.Should().Be("ECHO is ON.\r\n");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Theory]
    [InlineData("ECHO. > OUT.TXT", " \r\n")]
    [InlineData("ECHO.HELLO > OUT.TXT", "HELLO \r\n")]
    [InlineData("ECHO.  HELLO > OUT.TXT", "  HELLO \r\n")]
    public void HostRequestedBatch_EchoDotSeparatorOutputsCorrectText(string batchLine, string expectedOutput) {
        string tempDir = CreateTempDirectory("dos_batch_echo_dot");

        try {
            RunBatchScript(tempDir, batchLine + "\r\n");

            string output = File.ReadAllText(Path.Combine(tempDir, "OUT.TXT"));
            output.Should().Be(expectedOutput);
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_IfUnquotedComparisonExecutesCommand() {
        string tempDir = CreateTempDirectory("dos_batch_if_unquoted");

        try {
            File.WriteAllBytes(Path.Combine(tempDir, "WRITE.COM"), BuildVideoWriterCom('Q', 0));

            RunAndAssertVideoCellFromScript(tempDir, "IF ONE==ONE CALL WRITE.COM\r\n", 'Q');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Theory]
    [InlineData("FOR %C IN (ONE TWO) ECHO %C")]
    [InlineData("FOR %C (ONE TWO) DO ECHO %C")]
    [InlineData("FOR IN (ONE TWO) DO ECHO %C")]
    [InlineData("FOR %C IN ONE TWO DO ECHO %C")]
    [InlineData("FOR %C IN (ONE TWO) DO")]
    public void HostRequestedBatch_ForMalformedSyntaxSilentlySkips(string batchLine) {
        string tempDir = CreateTempDirectory("dos_batch_for_malformed");

        try {
            File.WriteAllBytes(Path.Combine(tempDir, "WRITE.COM"), BuildVideoWriterCom('Z', 0));

            RunAndAssertVideoCellNotWrittenFromScript(tempDir, batchLine + "\r\n", 'Z');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_SetWithoutArgumentsEnumeratesEnvironment() {
        string tempDir = CreateTempDirectory("dos_batch_set_enumerate");

        try {
            RunBatchScript(tempDir, "SET MYVAR=MYVALUE\r\nSET > VARS.TXT\r\n");

            string output = File.ReadAllText(Path.Combine(tempDir, "VARS.TXT"));
            output.Should().Contain("MYVAR=MYVALUE");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_ErrorlevelPropagatesFromCalledProgram() {
        string tempDir = CreateTempDirectory("dos_batch_errorlevel_propagate");

        try {
            File.WriteAllBytes(Path.Combine(tempDir, "EXIT42.COM"), BuildExitCodeCom(42));
            File.WriteAllBytes(Path.Combine(tempDir, "WRITE.COM"), BuildVideoWriterCom('E', 0));

            RunAndAssertVideoCellFromScript(tempDir, "CALL EXIT42.COM\r\nIF ERRORLEVEL 42 CALL WRITE.COM\r\n", 'E');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_CallNestedBatchPreservesContext() {
        string tempDir = CreateTempDirectory("dos_batch_nested_call");

        try {
            File.WriteAllBytes(Path.Combine(tempDir, "WRITE1.COM"), BuildVideoWriterCom('A', 0));
            File.WriteAllBytes(Path.Combine(tempDir, "WRITE2.COM"), BuildVideoWriterCom('B', 2));

            File.WriteAllText(Path.Combine(tempDir, "CHILD.BAT"), "CALL WRITE2.COM\r\n");
            string startBatchPath = Path.Combine(tempDir, "START.BAT");
            File.WriteAllText(startBatchPath, "CALL WRITE1.COM\r\nCALL CHILD.BAT\r\n");

            char[] cells = RunAndCaptureVideoCells(startBatchPath, tempDir, 2);
            cells[0].Should().Be('A');
            cells[1].Should().Be('B');
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    [Fact]
    public void HostRequestedBatch_VariableExpansionWorksAcrossBatchCalls() {
        string tempDir = CreateTempDirectory("dos_batch_var_expand_across");

        try {
            File.WriteAllText(Path.Combine(tempDir, "CHILD.BAT"), "ECHO %SHAREDVAR% > OUTPUT.TXT\r\n");
            RunBatchScript(tempDir, "SET SHAREDVAR=SHARED_VALUE\r\nCALL CHILD.BAT\r\n");

            string output = File.ReadAllText(Path.Combine(tempDir, "OUTPUT.TXT"));
            output.Should().Contain("SHARED_VALUE");
        } finally {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    private static void RunAndAssertVideoCell(string executablePath, string cDrivePath, char expectedChar) {
        char[] cells = RunAndCaptureVideoCells(executablePath, cDrivePath, 1);
        cells[0].Should().Be(expectedChar);
    }

    private static char[] RunAndCaptureVideoCells(string executablePath, string cDrivePath, int cellCount) {
        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: executablePath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: cDrivePath).Create();

        try {
            spice86.ProgramExecutor.Run();

            char[] cells = new char[cellCount];
            for (int i = 0; i < cellCount; i++) {
                uint videoAddress = MemoryUtils.ToPhysicalAddress(0xB800, (ushort)(i * 2));
                cells[i] = (char)spice86.Machine.Memory.UInt8[videoAddress];
            }

            return cells;
        } finally {
            spice86.Dispose();
        }
    }

    private static void RunWithoutVideoRead(string executablePath, string cDrivePath) {
        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: executablePath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: cDrivePath).Create();

        try {
            spice86.ProgramExecutor.Run();
        } finally {
            spice86.Dispose();
        }
    }

    private static void RunWithoutVideoReadNoPit(string executablePath, string cDrivePath) {
        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: executablePath,
            enablePit: false,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: cDrivePath).Create();

        try {
            spice86.ProgramExecutor.Run();
        } finally {
            spice86.Dispose();
        }
    }

    private static void RunBatchScript(string cDrivePath, string script) {
        string startBatchPath = Path.Combine(cDrivePath, "START.BAT");
        File.WriteAllText(startBatchPath, script);
        RunWithoutVideoRead(startBatchPath, cDrivePath);
    }

    private static void RunAndAssertVideoCellFromScript(string cDrivePath, string script, char expectedChar) {
        string startBatchPath = Path.Combine(cDrivePath, "START.BAT");
        File.WriteAllText(startBatchPath, script);
        RunAndAssertVideoCell(startBatchPath, cDrivePath, expectedChar);
    }

    private static void RunAndAssertVideoCellNotWrittenFromScript(string cDrivePath, string script, char unexpectedChar) {
        string startBatchPath = Path.Combine(cDrivePath, "START.BAT");
        File.WriteAllText(startBatchPath, script);
        char[] cells = RunAndCaptureVideoCells(startBatchPath, cDrivePath, 1);
        cells[0].Should().NotBe(unexpectedChar);
    }

    private static byte[] BuildVideoWriterCom(char value, ushort videoOffset) {
        return new byte[] {
            0xB8, 0x00, 0xB8,
            0x8E, 0xC0,
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),
            0xB0, (byte)value,
            0xB4, 0x07,
            0xAB,
            0xB8, 0x00, 0x4C,
            0xCD, 0x21
        };
    }

    private static byte[] BuildExitCodeCom(byte exitCode) {
        return new byte[] {
            0xB8, exitCode, 0x4C,
            0xCD, 0x21
        };
    }

    private static byte[] BuildStdoutWriterCom(string text) {
        byte[] ascii = System.Text.Encoding.ASCII.GetBytes(text);
        const int codeLength = 18;
        byte[] machineCode = new byte[codeLength + ascii.Length];
        ushort dataOffset = (ushort)(0x100 + codeLength);

        int i = 0;
        machineCode[i++] = 0xBB;
        machineCode[i++] = 0x01;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xBA;
        machineCode[i++] = (byte)(dataOffset & 0xFF);
        machineCode[i++] = (byte)(dataOffset >> 8);
        machineCode[i++] = 0xB9;
        machineCode[i++] = (byte)ascii.Length;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xB4;
        machineCode[i++] = 0x40;
        machineCode[i++] = 0xCD;
        machineCode[i++] = 0x21;
        machineCode[i++] = 0xB8;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0x4C;
        machineCode[i++] = 0xCD;
        machineCode[i++] = 0x21;

        Array.Copy(ascii, 0, machineCode, codeLength, ascii.Length);

        return machineCode;
    }

    private static byte[] BuildStdinToStdoutCom() {
        const int codeLength = 31;
        byte[] machineCode = new byte[codeLength + 1];
        ushort dataOffset = (ushort)(0x100 + codeLength);

        int i = 0;
        machineCode[i++] = 0xBB;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xBA;
        machineCode[i++] = (byte)(dataOffset & 0xFF);
        machineCode[i++] = (byte)(dataOffset >> 8);
        machineCode[i++] = 0xB9;
        machineCode[i++] = 0x01;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xB4;
        machineCode[i++] = 0x3F;
        machineCode[i++] = 0xCD;
        machineCode[i++] = 0x21;
        machineCode[i++] = 0xBB;
        machineCode[i++] = 0x01;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xBA;
        machineCode[i++] = (byte)(dataOffset & 0xFF);
        machineCode[i++] = (byte)(dataOffset >> 8);
        machineCode[i++] = 0xB9;
        machineCode[i++] = 0x01;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xB4;
        machineCode[i++] = 0x40;
        machineCode[i++] = 0xCD;
        machineCode[i++] = 0x21;
        machineCode[i++] = 0xB8;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0x4C;
        machineCode[i++] = 0xCD;
        machineCode[i++] = 0x21;

        return machineCode;
    }

    private static byte[] BuildStdinToVideoWriterCom(ushort videoOffset) {
        const int codeLength = 32;
        byte[] machineCode = new byte[codeLength + 1];
        ushort dataOffset = (ushort)(0x100 + codeLength);

        int i = 0;
        machineCode[i++] = 0xBB;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xBA;
        machineCode[i++] = (byte)(dataOffset & 0xFF);
        machineCode[i++] = (byte)(dataOffset >> 8);
        machineCode[i++] = 0xB9;
        machineCode[i++] = 0x01;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xB4;
        machineCode[i++] = 0x3F;
        machineCode[i++] = 0xCD;
        machineCode[i++] = 0x21;
        machineCode[i++] = 0xB8;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0xB8;
        machineCode[i++] = 0x8E;
        machineCode[i++] = 0xC0;
        machineCode[i++] = 0xBF;
        machineCode[i++] = (byte)(videoOffset & 0xFF);
        machineCode[i++] = (byte)(videoOffset >> 8);
        machineCode[i++] = 0xA0;
        machineCode[i++] = (byte)(dataOffset & 0xFF);
        machineCode[i++] = (byte)(dataOffset >> 8);
        machineCode[i++] = 0xB4;
        machineCode[i++] = 0x07;
        machineCode[i++] = 0xAB;
        machineCode[i++] = 0xB8;
        machineCode[i++] = 0x00;
        machineCode[i++] = 0x4C;
        machineCode[i++] = 0xCD;
        machineCode[i++] = 0x21;

        return machineCode;
    }

    private static string CreateTempDirectory(string prefix) {
        string tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void DeleteDirectoryIfExists(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, true);
        }
    }
}