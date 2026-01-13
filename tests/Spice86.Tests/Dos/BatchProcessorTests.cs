namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Unit tests for the <see cref="BatchProcessor"/> class.
/// Tests batch file processing features based on DOS batch semantics.
/// </summary>
public class BatchProcessorTests : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly string _tempDir;

    public BatchProcessorTests() {
        _loggerService = Substitute.For<ILoggerService>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"Spice86BatchTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        try {
            if (Directory.Exists(_tempDir)) {
                Directory.Delete(_tempDir, recursive: true);
            }
        } catch (IOException) {
            // Ignore cleanup errors - file system issues like locked files or permission problems
            // shouldn't fail the test
        } catch (UnauthorizedAccessException) {
            // Ignore permission issues during cleanup
        }
    }

    private string CreateBatchFile(string name, string content) {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void StartBatch_WithValidFile_ReturnsTrue() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string batchPath = CreateBatchFile("test.bat", "echo hello");

        // Act
        bool result = processor.StartBatch(batchPath, []);

        // Assert
        result.Should().BeTrue();
        processor.IsProcessingBatch.Should().BeTrue();
        processor.CurrentBatchPath.Should().Be(batchPath);
    }

    [Fact]
    public void StartBatch_WithInvalidFile_ReturnsFalse() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string invalidPath = Path.Combine(_tempDir, "nonexistent.bat");

        // Act
        bool result = processor.StartBatch(invalidPath, []);

        // Assert
        result.Should().BeFalse();
        processor.IsProcessingBatch.Should().BeFalse();
    }

    [Fact]
    public void StartBatch_WithEmptyPath_ReturnsFalse() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        bool result = processor.StartBatch("", []);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ReadNextLine_SkipsEmptyLines() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "\n\necho hello\n\n";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out bool shouldEcho);

        // Assert
        line.Should().Be("echo hello");
        shouldEcho.Should().BeTrue(); // ECHO is ON by default
    }

    [Fact]
    public void ReadNextLine_SkipsLabelLines() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = ":label\necho hello";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo hello");
    }

    [Fact]
    public void ReadNextLine_SkipsRemComments() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "REM this is a comment\necho hello";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo hello");
    }

    [Fact]
    public void ReadNextLine_AtSymbolSuppressesEcho() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "@echo off";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out bool shouldEcho);

        // Assert
        line.Should().Be("echo off");
        shouldEcho.Should().BeFalse(); // @ suppresses echo
    }

    [Fact]
    public void ReadNextLine_ExpandsParameter0() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo %0";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be($"echo {batchPath}");
    }

    [Fact]
    public void ReadNextLine_ExpandsParameters1Through9() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo %1 %2 %3";
        string batchPath = CreateBatchFile("test.bat", content);
        string[] args = ["arg1", "arg2", "arg3"];
        processor.StartBatch(batchPath, args);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo arg1 arg2 arg3");
    }

    [Fact]
    public void ReadNextLine_UndefinedParameterExpandsToEmpty() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo %9";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, ["only_one_arg"]);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo ");
    }

    [Fact]
    public void ReadNextLine_DoublePercentExpandsToSinglePercent() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo 50%% complete";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo 50% complete");
    }

    [Fact]
    public void ReadNextLine_ReturnsNullAtEndOfFile() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo hello";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);
        
        // Read the first line
        processor.ReadNextLine(out _);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().BeNull();
        processor.IsProcessingBatch.Should().BeFalse();
    }

    [Fact]
    public void ParseCommand_EchoOff_SetsEchoStateFalse() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        processor.Echo.Should().BeTrue(); // Default is ON

        // Act
        BatchCommand cmd = processor.ParseCommand("echo off");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty); // ECHO OFF is handled internally
        processor.Echo.Should().BeFalse();
    }

    [Fact]
    public void ParseCommand_EchoOn_SetsEchoStateTrue() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        processor.Echo = false; // Set to OFF first

        // Act
        BatchCommand cmd = processor.ParseCommand("echo on");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty);
        processor.Echo.Should().BeTrue();
    }

    [Fact]
    public void ParseCommand_EchoWithMessage_ReturnsPrintMessage() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("echo Hello World");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.PrintMessage);
        cmd.Value.Should().Be("Hello World");
    }

    [Fact]
    public void ParseCommand_EchoWithoutArgs_ReturnsShowEchoState() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("echo");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.ShowEchoState);
    }

    [Fact]
    public void ParseCommand_EchoDot_PrintsEmptyLine() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("echo.");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.PrintMessage);
        cmd.Value.Should().Be(""); // Empty line
    }

    [Fact]
    public void ParseCommand_ExternalCommand_ReturnsExecuteProgram() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("maupiti1");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.ExecuteProgram);
        cmd.Value.Should().Be("maupiti1");
        cmd.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void ParseCommand_ExternalCommandWithArgs_ReturnsExecuteProgramWithArgs() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("maup arg1 arg2");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.ExecuteProgram);
        cmd.Value.Should().Be("maup");
        cmd.Arguments.Should().Be("arg1 arg2");
    }

    [Fact]
    public void ParseCommand_Goto_ReturnsGotoCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("goto end");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Goto);
        cmd.Value.Should().Be("end");
    }

    [Fact]
    public void ParseCommand_GotoWithColon_RemovesColon() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("goto :end");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Goto);
        cmd.Value.Should().Be("end");
    }

    [Fact]
    public void ParseCommand_Call_ReturnsCallBatchCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("call other.bat");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.CallBatch);
        cmd.Value.Should().Be("other.bat");
    }

    [Fact]
    public void ParseCommand_CallWithArgs_ReturnsCallBatchWithArgs() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("call other.bat arg1 arg2");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.CallBatch);
        cmd.Value.Should().Be("other.bat");
        cmd.Arguments.Should().Be("arg1 arg2");
    }

    [Fact]
    public void ParseCommand_Rem_ReturnsEmpty() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("rem this is a comment");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty);
    }

    [Fact]
    public void GotoLabel_FindsLabel() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo before\n:target\necho after";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        bool found = processor.GotoLabel("target");

        // Assert
        found.Should().BeTrue();

        // The next line should be after the label
        string? line = processor.ReadNextLine(out _);
        line.Should().Be("echo after");
    }

    [Fact]
    public void GotoLabel_IsCaseInsensitive() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = ":TARGET\necho found";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        bool found = processor.GotoLabel("target");

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public void GotoLabel_ReturnsFalseForMissingLabel() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo hello";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        bool found = processor.GotoLabel("nonexistent");

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void ExitBatch_RestoresEchoState() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        processor.Echo = true; // Start with ECHO ON
        
        string content = "@echo off\necho hello";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Read and process echo off
        string? line = processor.ReadNextLine(out _);
        processor.ParseCommand(line!);
        processor.Echo.Should().BeFalse(); // ECHO is now OFF

        // Act
        processor.ExitBatch();

        // Assert
        processor.Echo.Should().BeTrue(); // Restored to ON
        processor.IsProcessingBatch.Should().BeFalse();
    }

    [Fact]
    public void NestedBatchFiles_ChainCorrectly() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        
        string innerContent = "echo inner";
        string innerPath = CreateBatchFile("inner.bat", innerContent);
        
        string outerContent = "echo outer\ncall inner.bat\necho back";
        string outerPath = CreateBatchFile("outer.bat", outerContent);

        processor.StartBatch(outerPath, []);
        processor.CurrentBatchPath.Should().Be(outerPath);

        // Start inner batch (simulating CALL)
        processor.StartBatch(innerPath, []);
        processor.CurrentBatchPath.Should().Be(innerPath);

        // Exit inner batch
        processor.ExitBatch();
        
        // Should return to outer batch
        processor.IsProcessingBatch.Should().BeTrue();
        processor.CurrentBatchPath.Should().Be(outerPath);
    }

    [Fact]
    public void IntegrationTest_SimpleBatchFile() {
        // Test the example batch file from the problem statement:
        // echo off
        // maupiti1
        // maup %1

        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo off\nmaupiti1\nmaup %1";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, ["argument1"]);

        // Act & Assert - Line 1: "echo off"
        string? line1 = processor.ReadNextLine(out bool echo1);
        echo1.Should().BeTrue(); // First line is echoed (ECHO is still ON)
        line1.Should().Be("echo off");
        
        BatchCommand cmd1 = processor.ParseCommand(line1!);
        cmd1.Type.Should().Be(BatchCommandType.Empty);
        processor.Echo.Should().BeFalse(); // Now ECHO is OFF

        // Act & Assert - Line 2: "maupiti1"
        string? line2 = processor.ReadNextLine(out bool echo2);
        echo2.Should().BeFalse(); // Not echoed because ECHO is OFF
        line2.Should().Be("maupiti1");
        
        BatchCommand cmd2 = processor.ParseCommand(line2!);
        cmd2.Type.Should().Be(BatchCommandType.ExecuteProgram);
        cmd2.Value.Should().Be("maupiti1");

        // Act & Assert - Line 3: "maup %1" -> "maup argument1"
        string? line3 = processor.ReadNextLine(out bool echo3);
        echo3.Should().BeFalse();
        line3.Should().Be("maup argument1");
        
        BatchCommand cmd3 = processor.ParseCommand(line3!);
        cmd3.Type.Should().Be(BatchCommandType.ExecuteProgram);
        cmd3.Value.Should().Be("maup");
        cmd3.Arguments.Should().Be("argument1");

        // Act & Assert - End of file
        string? line4 = processor.ReadNextLine(out _);
        line4.Should().BeNull();
        processor.IsProcessingBatch.Should().BeFalse();
    }

    #region SET Command Tests

    [Fact]
    public void ParseCommand_SetWithoutArgs_ReturnsShowVariables() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("SET");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.ShowVariables);
    }

    [Fact]
    public void ParseCommand_SetWithName_ReturnsShowVariable() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("SET PATH");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.ShowVariable);
        cmd.Value.Should().Be("PATH");
    }

    [Fact]
    public void ParseCommand_SetWithNameValue_ReturnsSetVariable() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("SET PATH=C:\\DOS");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.SetVariable);
        cmd.Value.Should().Be("PATH");
        cmd.Arguments.Should().Be("C:\\DOS");
    }

    [Fact]
    public void ParseCommand_SetWithEmptyValue_ReturnsSetVariable() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("SET PATH=");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.SetVariable);
        cmd.Value.Should().Be("PATH");
        cmd.Arguments.Should().BeEmpty();
    }

    #endregion

    #region IF Command Tests

    [Fact]
    public void ParseCommand_IfExist_ReturnsIfCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF EXIST test.txt echo found");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("EXIST");
        cmd.Arguments.Should().StartWith("test.txt echo found");
        cmd.Negate.Should().BeFalse();
    }

    [Fact]
    public void ParseCommand_IfNotExist_ReturnsIfCommandWithNegation() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF NOT EXIST test.txt echo not found");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("EXIST");
        cmd.Arguments.Should().StartWith("test.txt echo not found");
        cmd.Negate.Should().BeTrue();
    }

    [Fact]
    public void ParseCommand_IfErrorlevel_ReturnsIfCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF ERRORLEVEL 1 echo error");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("ERRORLEVEL");
        cmd.Arguments.Should().StartWith("1 echo error");
    }

    [Fact]
    public void ParseCommand_IfStringCompare_ReturnsIfCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF %1==test echo matched");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("COMPARE");
    }

    #endregion

    #region SHIFT Command Tests

    [Fact]
    public void ParseCommand_Shift_ReturnsShiftCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("SHIFT");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Shift);
    }

    [Fact]
    public void ReadNextLine_AfterShift_ShiftsParameters() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo %1 %2 %3\nshift\necho %1 %2 %3";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, ["a", "b", "c", "d"]);

        // Act - First line with original parameters
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be("echo a b c");

        // Read SHIFT command
        string? shiftLine = processor.ReadNextLine(out _);
        shiftLine.Should().Be("shift");
        processor.ParseCommand(shiftLine!); // This triggers the shift

        // Act - After shift, parameters are shifted
        string? line2 = processor.ReadNextLine(out _);
        line2.Should().Be("echo b c d");
    }

    #endregion

    #region PAUSE Command Tests

    [Fact]
    public void ParseCommand_Pause_ReturnsPauseCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("PAUSE");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Pause);
    }

    #endregion

    #region EXIT Command Tests

    [Fact]
    public void ParseCommand_Exit_ReturnsExitCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("EXIT");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Exit);
    }

    #endregion

    #region Environment Variable Tests

    [Fact]
    public void ReadNextLine_WithEnvironmentVariable_ExpandsVariable() {
        // Arrange
        TestBatchEnvironment env = new();
        env.SetVariable("MYVAR", "TestValue");
        BatchProcessor processor = new(_loggerService, env);
        string content = "echo %MYVAR%";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo TestValue");
    }

    [Fact]
    public void ReadNextLine_WithMultipleEnvironmentVariables_ExpandsAll() {
        // Arrange
        TestBatchEnvironment env = new();
        env.SetVariable("VAR1", "Hello");
        env.SetVariable("VAR2", "World");
        BatchProcessor processor = new(_loggerService, env);
        string content = "echo %VAR1% %VAR2%";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo Hello World");
    }

    [Fact]
    public void ReadNextLine_WithUndefinedEnvironmentVariable_LeavesEmpty() {
        // Arrange
        TestBatchEnvironment env = new();
        BatchProcessor processor = new(_loggerService, env);
        string content = "echo %UNDEFINED%";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo ");
    }

    [Fact]
    public void ReadNextLine_WithMixedParametersAndEnvironment_ExpandsBoth() {
        // Arrange
        TestBatchEnvironment env = new();
        env.SetVariable("PATH", "C:\\DOS");
        BatchProcessor processor = new(_loggerService, env);
        string content = "echo %0 %1 %PATH%";
        string batchPath = CreateBatchFile("test.bat", content);
        processor.StartBatch(batchPath, ["arg1"]);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be($"echo {batchPath} arg1 C:\\DOS");
    }

    #endregion

    #region IBatchLineReader Interface Tests

    [Fact]
    public void StartBatchWithReader_UsesCustomReader() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string[] lines = ["echo hello", "echo world"];
        TestStringLineReader reader = new(lines);

        // Act
        bool result = processor.StartBatchWithReader("test.bat", [], reader);
        string? line1 = processor.ReadNextLine(out _);
        string? line2 = processor.ReadNextLine(out _);

        // Assert
        result.Should().BeTrue();
        line1.Should().Be("echo hello");
        line2.Should().Be("echo world");
    }

    #endregion

    #region FOR Command Tests

    [Fact]
    public void ParseCommand_ForWithValidSyntax_ReturnsForCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %C IN (one two three) DO echo %C");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.For);
        cmd.Value.Should().Be("%C");
        cmd.GetForSet().Should().BeEquivalentTo(["one", "two", "three"]);
        cmd.GetForCommand().Should().Be("echo %C");
    }

    [Fact]
    public void ParseCommand_ForWithCommaDelimiters_ReturnsForCommandWithCorrectSet() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %X IN (a,b,c) DO dir %X");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.For);
        cmd.Value.Should().Be("%X");
        cmd.GetForSet().Should().BeEquivalentTo(["a", "b", "c"]);
        cmd.GetForCommand().Should().Be("dir %X");
    }

    [Fact]
    public void ParseCommand_ForMissingVariable_ReturnsEmptyCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR IN (one two) DO echo");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty);
    }

    [Fact]
    public void ParseCommand_ForMissingIn_ReturnsEmptyCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %C (one two) DO echo");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty);
    }

    [Fact]
    public void ParseCommand_ForMissingParentheses_ReturnsEmptyCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %C IN one two DO echo");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty);
    }

    [Fact]
    public void ParseCommand_ForMissingDo_ReturnsEmptyCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %C IN (one two) echo");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty);
    }

    [Fact]
    public void ParseCommand_ForMissingCommand_ReturnsEmptyCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %C IN (one two) DO");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.Empty);
    }

    [Fact]
    public void ParseCommand_ForCaseInsensitive_ReturnsForCommand() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("for %c in (x y z) do echo %c");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.For);
    }

    #endregion

    #region AutoexecGenerator Tests

    [Fact]
    public void AutoexecGenerator_DefaultSettings_HasEchoOff() {
        // Arrange
        AutoexecGenerator generator = new();

        // Act
        string[] lines = generator.Generate();

        // Assert
        lines.Should().HaveCount(1);
        lines[0].Should().Be("@ECHO OFF");
    }

    [Fact]
    public void AutoexecGenerator_ForProgram_GeneratesCorrectContent() {
        // Arrange & Act
        AutoexecGenerator generator = AutoexecGenerator.ForProgram("GAME.EXE", "arg1 arg2");
        string[] lines = generator.Generate();

        // Assert
        lines.Should().HaveCount(3);
        lines[0].Should().Be("@ECHO OFF");
        lines[1].Should().Be("GAME.EXE arg1 arg2");
        lines[2].Should().Be("@EXIT");
    }

    [Fact]
    public void AutoexecGenerator_ForBatch_GeneratesCallCommand() {
        // Arrange & Act
        AutoexecGenerator generator = AutoexecGenerator.ForBatch("SETUP.BAT", "%1");
        string[] lines = generator.Generate();

        // Assert
        lines.Should().HaveCount(3);
        lines[0].Should().Be("@ECHO OFF");
        lines[1].Should().Be("CALL SETUP.BAT %1");
        lines[2].Should().Be("@EXIT");
    }

    [Fact]
    public void AutoexecGenerator_WithEnvironmentVariable_IncludesSetCommand() {
        // Arrange
        AutoexecGenerator generator = new();
        generator.SetEnvironmentVariable("TEMP", "C:\\TEMP");
        generator.AddProgramExecution("MYPROG.EXE");

        // Act
        string[] lines = generator.Generate();

        // Assert
        lines.Should().HaveCount(3);
        lines[0].Should().Be("@ECHO OFF");
        lines[1].Should().Be("@SET TEMP=C:\\TEMP");
        lines[2].Should().Be("MYPROG.EXE");
    }

    [Fact]
    public void AutoexecGenerator_SetPath_SetsPATHVariable() {
        // Arrange
        AutoexecGenerator generator = new();
        generator.SetPath("C:\\DOS;C:\\UTILS");
        generator.AddProgramExecution("MYPROG.EXE");

        // Act
        string[] lines = generator.Generate();

        // Assert
        lines.Should().Contain("@SET PATH=C:\\DOS;C:\\UTILS");
    }

    [Fact]
    public void AutoexecGenerator_GenerateAsString_UsesDosLineEndings() {
        // Arrange
        AutoexecGenerator generator = AutoexecGenerator.ForProgram("TEST.EXE");

        // Act
        string content = generator.GenerateAsString();

        // Assert
        content.Should().Contain("\r\n");
        content.Should().NotContain("\n\r"); // Not reversed
    }

    [Fact]
    public void AutoexecGenerator_WithoutExitAfter_DoesNotIncludeExit() {
        // Arrange & Act
        AutoexecGenerator generator = AutoexecGenerator.ForProgram("GAME.EXE", "", exitAfter: false);
        string[] lines = generator.Generate();

        // Assert
        lines.Should().HaveCount(2);
        lines.Should().NotContain("@EXIT");
    }

    #endregion

    #region StringArrayLineReader Tests

    [Fact]
    public void StringArrayLineReader_ReadsAllLines() {
        // Arrange
        string[] lines = ["line 1", "line 2", "line 3"];
        StringArrayLineReader reader = new(lines);

        // Act & Assert
        reader.ReadLine().Should().Be("line 1");
        reader.ReadLine().Should().Be("line 2");
        reader.ReadLine().Should().Be("line 3");
        reader.ReadLine().Should().BeNull();
    }

    [Fact]
    public void StringArrayLineReader_Reset_RestartsFromBeginning() {
        // Arrange
        string[] lines = ["first", "second"];
        StringArrayLineReader reader = new(lines);

        // Act
        reader.ReadLine(); // Read first
        reader.ReadLine(); // Read second
        bool resetResult = reader.Reset();
        string? afterReset = reader.ReadLine();

        // Assert
        resetResult.Should().BeTrue();
        afterReset.Should().Be("first");
    }

    [Fact]
    public void StringArrayLineReader_EmptyArray_ReturnsNullImmediately() {
        // Arrange
        StringArrayLineReader reader = new([]);

        // Act & Assert
        reader.ReadLine().Should().BeNull();
    }

    #endregion

    #region DosEnvironmentAdapter Tests

    [Fact]
    public void DosEnvironmentAdapter_CallsDelegate() {
        // Arrange
        Dictionary<string, string> env = new(StringComparer.OrdinalIgnoreCase) {
            { "PATH", "C:\\DOS" },
            { "TEMP", "C:\\TEMP" }
        };
        DosEnvironmentAdapter adapter = new(name =>
            env.TryGetValue(name, out string? value) ? value : null);

        // Act & Assert
        adapter.GetEnvironmentValue("PATH").Should().Be("C:\\DOS");
        adapter.GetEnvironmentValue("path").Should().Be("C:\\DOS"); // Case insensitive lookup
        adapter.GetEnvironmentValue("TEMP").Should().Be("C:\\TEMP");
        adapter.GetEnvironmentValue("UNDEFINED").Should().BeNull();
    }

    #endregion

    #region CommandCom Integration Tests

    [Fact]
    public void CommandCom_CreateAutoexecForProgram_ReturnsConfiguredGenerator() {
        // Act
        AutoexecGenerator generator = AutoexecGenerator.ForProgram("GAME.EXE", "arg1");
        string[] lines = generator.Generate();

        // Assert
        lines.Should().HaveCount(3);
        lines[0].Should().Be("@ECHO OFF");
        lines[1].Should().Be("GAME.EXE arg1");
        lines[2].Should().Be("@EXIT");
    }

    [Fact]
    public void CommandCom_CreateAutoexecForBatch_ReturnsConfiguredGenerator() {
        // Act
        AutoexecGenerator generator = AutoexecGenerator.ForBatch("SETUP.BAT", "%1");
        string[] lines = generator.Generate();

        // Assert
        lines.Should().HaveCount(3);
        lines[0].Should().Be("@ECHO OFF");
        lines[1].Should().Be("CALL SETUP.BAT %1");
        lines[2].Should().Be("@EXIT");
    }

    #endregion
}
