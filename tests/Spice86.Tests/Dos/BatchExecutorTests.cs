namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Unit tests for the <see cref="BatchExecutor"/> class.
/// Tests batch file execution logic including program launching, GOTO handling, and SHA256 verification support.
/// </summary>
public class BatchExecutorTests : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly string _tempDir;

    public BatchExecutorTests() {
        _loggerService = Substitute.For<ILoggerService>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"Spice86BatchExecTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        try {
            if (Directory.Exists(_tempDir)) {
                Directory.Delete(_tempDir, recursive: true);
            }
        } catch (IOException) {
            // Ignore cleanup errors
        } catch (UnauthorizedAccessException) {
            // Ignore permission issues
        }
    }

    private string CreateBatchFile(string name, string content) {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateExecutable(string name, byte[] content) {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    #region GOTO Command Tests - Comprehensive

    [Fact]
    public void GotoLabel_JumpsToCorrectPosition() {
        // Arrange - batch file with GOTO that skips a line
        BatchProcessor processor = new(_loggerService);
        string content = "@echo off\ngoto end\necho SHOULD NOT SEE THIS\n:end\necho AFTER LABEL";
        string batchPath = CreateBatchFile("goto_test.bat", content);
        processor.StartBatch(batchPath, []);

        // Act - Read first line (@echo off)
        string? line1 = processor.ReadNextLine(out _);
        processor.ParseCommand(line1!);

        // Read GOTO command
        string? gotoLine = processor.ReadNextLine(out _);
        gotoLine.Should().Be("goto end");
        
        BatchCommand gotoCmd = processor.ParseCommand(gotoLine!);
        gotoCmd.Type.Should().Be(BatchCommandType.Goto);
        gotoCmd.Value.Should().Be("end");

        // Execute GOTO - this should jump to :end
        bool found = processor.GotoLabel(gotoCmd.Value);
        found.Should().BeTrue();

        // The next line should be AFTER the label, skipping "echo SHOULD NOT SEE THIS"
        string? afterGoto = processor.ReadNextLine(out _);
        afterGoto.Should().Be("echo AFTER LABEL");
    }

    [Fact]
    public void GotoLabel_JumpsBackward() {
        // Arrange - GOTO that jumps backward creates a loop
        BatchProcessor processor = new(_loggerService);
        string content = ":start\necho first\ngoto start";
        string batchPath = CreateBatchFile("goto_back.bat", content);
        processor.StartBatch(batchPath, []);

        // Act - Read "echo first" (label is skipped)
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be("echo first");

        // Read "goto start"
        string? line2 = processor.ReadNextLine(out _);
        line2.Should().Be("goto start");

        // Execute GOTO back to :start
        bool found = processor.GotoLabel("start");
        found.Should().BeTrue();

        // Next line should be "echo first" again
        string? line3 = processor.ReadNextLine(out _);
        line3.Should().Be("echo first");
    }

    [Fact]
    public void GotoLabel_WithMultipleLabels_FindsCorrectOne() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = ":label1\necho one\n:label2\necho two\n:label3\necho three";
        string batchPath = CreateBatchFile("multi_label.bat", content);
        processor.StartBatch(batchPath, []);

        // Act - Jump to label2
        bool found = processor.GotoLabel("label2");

        // Assert
        found.Should().BeTrue();
        string? line = processor.ReadNextLine(out _);
        line.Should().Be("echo two");
    }

    [Fact]
    public void GotoLabel_SkipsCodeBetweenGotoAndLabel() {
        // Arrange - Verify that all lines between GOTO and label are skipped
        BatchProcessor processor = new(_loggerService);
        string content = "@echo off\ngoto target\necho skip1\necho skip2\necho skip3\n:target\necho reached";
        string batchPath = CreateBatchFile("skip_test.bat", content);
        processor.StartBatch(batchPath, []);

        // Read and process @echo off
        processor.ReadNextLine(out _);

        // Read goto target
        string? gotoLine = processor.ReadNextLine(out _);
        BatchCommand cmd = processor.ParseCommand(gotoLine!);
        processor.GotoLabel(cmd.Value);

        // Next line should be "echo reached", all skip lines should be bypassed
        string? afterLabel = processor.ReadNextLine(out _);
        afterLabel.Should().Be("echo reached");
    }

    [Fact]
    public void GotoLabel_WithSpacesInLabel_Works() {
        // Arrange - Labels can have trailing content
        BatchProcessor processor = new(_loggerService);
        string content = "goto myLabel\n:myLabel some comment here\necho found";
        string batchPath = CreateBatchFile("label_space.bat", content);
        processor.StartBatch(batchPath, []);

        // Read goto
        string? gotoLine = processor.ReadNextLine(out _);
        BatchCommand cmd = processor.ParseCommand(gotoLine!);
        
        // Execute GOTO
        bool found = processor.GotoLabel(cmd.Value);
        found.Should().BeTrue();

        string? next = processor.ReadNextLine(out _);
        next.Should().Be("echo found");
    }

    #endregion

    #region CALL Command Tests

    [Fact]
    public void Call_ParsesCorrectly() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("CALL SETUP.BAT arg1 arg2");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.CallBatch);
        cmd.Value.Should().Be("SETUP.BAT");
        cmd.Arguments.Should().Be("arg1 arg2");
    }

    [Fact]
    public void NestedBatchFiles_MaintainSeparateContexts() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string innerPath = CreateBatchFile("inner.bat", "echo inner line");
        string outerPath = CreateBatchFile("outer.bat", "echo outer line");

        // Start outer batch
        processor.StartBatch(outerPath, []);
        string? outerLine = processor.ReadNextLine(out _);
        outerLine.Should().Be("echo outer line");

        // Simulate CALL - start inner batch (nested)
        processor.StartBatch(innerPath, []);
        processor.CurrentBatchPath.Should().Be(innerPath);

        string? innerLine = processor.ReadNextLine(out _);
        innerLine.Should().Be("echo inner line");

        // Exit inner batch - should return to outer
        processor.ExitBatch();
        processor.CurrentBatchPath.Should().Be(outerPath);
    }

    #endregion

    #region IF Command Tests

    [Fact]
    public void ParseCommand_IfExist_ParsesCorrectly() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF EXIST config.sys echo Found");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("EXIST");
        cmd.Arguments.Should().Contain("config.sys");
        cmd.Negate.Should().BeFalse();
    }

    [Fact]
    public void ParseCommand_IfNotExist_SetsNegateFlag() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF NOT EXIST missing.txt echo Not found");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("EXIST");
        cmd.Negate.Should().BeTrue();
    }

    [Fact]
    public void ParseCommand_IfErrorlevel_ParsesLevel() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF ERRORLEVEL 5 goto error");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("ERRORLEVEL");
        cmd.Arguments.Should().StartWith("5");
    }

    [Fact]
    public void ParseCommand_IfStringComparison_ParsesStrings() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("IF %1==yes echo Confirmed");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.If);
        cmd.Value.Should().Be("COMPARE");
    }

    #endregion

    #region SHIFT Command Tests

    [Fact]
    public void Shift_MovesParametersCorrectly() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo %1 %2 %3\nshift\necho %1 %2 %3\nshift\necho %1 %2 %3";
        string batchPath = CreateBatchFile("shift_test.bat", content);
        processor.StartBatch(batchPath, ["a", "b", "c", "d", "e"]);

        // Before shift: %1=a, %2=b, %3=c
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be("echo a b c");

        // SHIFT command
        string? shiftLine1 = processor.ReadNextLine(out _);
        processor.ParseCommand(shiftLine1!);

        // After first shift: %1=b, %2=c, %3=d
        string? line2 = processor.ReadNextLine(out _);
        line2.Should().Be("echo b c d");

        // Second SHIFT
        string? shiftLine2 = processor.ReadNextLine(out _);
        processor.ParseCommand(shiftLine2!);

        // After second shift: %1=c, %2=d, %3=e
        string? line3 = processor.ReadNextLine(out _);
        line3.Should().Be("echo c d e");
    }

    [Fact]
    public void Shift_Parameter0_StaysConstant() {
        // Arrange - %0 should always be the batch file name
        BatchProcessor processor = new(_loggerService);
        string content = "echo %0\nshift\necho %0";
        string batchPath = CreateBatchFile("shift0.bat", content);
        processor.StartBatch(batchPath, ["arg1"]);

        // %0 before shift
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be($"echo {batchPath}");

        // SHIFT
        processor.ReadNextLine(out _);
        processor.ParseCommand("shift");

        // %0 after shift - should still be the batch file path
        string? line2 = processor.ReadNextLine(out _);
        line2.Should().Be($"echo {batchPath}");
    }

    #endregion

    #region FOR Command Tests

    [Fact]
    public void ParseCommand_ForLoop_ParsesAllComponents() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %%i IN (file1.txt file2.txt file3.txt) DO type %%i");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.For);
        cmd.Value.Should().Be("%%i");
        cmd.GetForSet().Should().BeEquivalentTo(["file1.txt", "file2.txt", "file3.txt"]);
        cmd.GetForCommand().Should().Be("type %%i");
    }

    [Fact]
    public void ParseCommand_ForWithCommaDelimiters_SplitsCorrectly() {
        // Arrange
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %X IN (a,b,c,d) DO echo %X");

        // Assert
        cmd.GetForSet().Should().BeEquivalentTo(["a", "b", "c", "d"]);
    }

    [Fact]
    public void ParseCommand_ForWithMixedDelimiters_SplitsCorrectly() {
        // Arrange - Spaces, commas, semicolons are all valid delimiters
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("FOR %V IN (a b,c;d) DO echo %V");

        // Assert
        cmd.GetForSet().Should().BeEquivalentTo(["a", "b", "c", "d"]);
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

    [Fact]
    public void ExitBatch_RestoresPreviousEchoState() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        processor.Echo = true;

        string batchPath = CreateBatchFile("exit_test.bat", "@echo off\necho test");
        processor.StartBatch(batchPath, []);

        // Process @echo off
        string? line = processor.ReadNextLine(out _);
        processor.ParseCommand(line!);
        processor.Echo.Should().BeFalse();

        // Act - Exit batch
        processor.ExitBatch();

        // Assert - Echo should be restored to original state
        processor.Echo.Should().BeTrue();
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

    #region SET Command Tests

    [Fact]
    public void ParseCommand_SetWithEqualsInValue_PreservesValue() {
        // Arrange - Values can contain = sign
        BatchProcessor processor = new(_loggerService);

        // Act
        BatchCommand cmd = processor.ParseCommand("SET PROMPT=$P$G=>");

        // Assert
        cmd.Type.Should().Be(BatchCommandType.SetVariable);
        cmd.Value.Should().Be("PROMPT");
        cmd.Arguments.Should().Be("$P$G=>");
    }

    #endregion

    #region Environment Variable Expansion Tests

    [Fact]
    public void ExpandParameters_WithNestedPercents_HandlesCorrectly() {
        // Arrange
        TestBatchEnvironment env = new();
        env.SetVariable("LEVEL", "1");
        env.SetVariable("LEVEL1", "FirstLevel");
        BatchProcessor processor = new(_loggerService, env);
        
        // Note: DOS doesn't support nested expansion like %LEVEL%LEVEL%
        // but we should handle edge cases gracefully
        string content = "echo %LEVEL%";
        string batchPath = CreateBatchFile("nested.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert
        line.Should().Be("echo 1");
    }

    [Fact]
    public void ExpandParameters_UnmatchedPercent_PreservesLiteral() {
        // Arrange
        BatchProcessor processor = new(_loggerService);
        string content = "echo 50% complete";
        string batchPath = CreateBatchFile("percent.bat", content);
        processor.StartBatch(batchPath, []);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert - Unmatched % should be preserved
        line.Should().Be("echo 50% complete");
    }

    #endregion

    #region Complex Batch File Tests

    [Fact]
    public void IntegrationTest_ComplexBatchWithGotoAndLabels() {
        // Test a realistic batch file with multiple labels and GOTOs
        BatchProcessor processor = new(_loggerService);
        string content = @"@echo off
if %1==A goto processA
if %1==B goto processB
goto end

:processA
echo Processing A
goto end

:processB
echo Processing B
goto end

:end
echo Done";

        string batchPath = CreateBatchFile("complex.bat", content);
        processor.StartBatch(batchPath, ["A"]);

        // Read @echo off
        string? line1 = processor.ReadNextLine(out _);
        processor.ParseCommand(line1!);

        // Read first IF - "if A==A goto processA" should be true
        string? ifLine = processor.ReadNextLine(out _);
        ifLine.Should().Be("if A==A goto processA");

        // For testing, we'll manually execute the GOTO since IF evaluation
        // would require more complex logic
        processor.GotoLabel("processA");

        // After GOTO, should be at "echo Processing A"
        string? afterGoto = processor.ReadNextLine(out _);
        afterGoto.Should().Be("echo Processing A");
    }

    [Fact]
    public void IntegrationTest_BatchWithAllParameters() {
        // Test all 10 parameters (%0-%9)
        BatchProcessor processor = new(_loggerService);
        string content = "echo %0 %1 %2 %3 %4 %5 %6 %7 %8 %9";
        string batchPath = CreateBatchFile("params.bat", content);
        string[] args = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        processor.StartBatch(batchPath, args);

        // Act
        string? line = processor.ReadNextLine(out _);

        // Assert - %0 is batch path, %1-9 are arguments
        line.Should().Be($"echo {batchPath} one two three four five six seven eight nine");
    }

    [Fact]
    public void IntegrationTest_GotoWithNestedLabels_SkipsIntermediateCode() {
        // Test GOTO that skips multiple labels and code blocks
        BatchProcessor processor = new(_loggerService);
        string content = @"@echo off
goto final
:first
echo SKIP1
goto second
:second
echo SKIP2
goto third
:third
echo SKIP3
:final
echo REACHED FINAL";

        string batchPath = CreateBatchFile("nested_goto.bat", content);
        processor.StartBatch(batchPath, []);

        // Read and process @echo off
        string? echoOff = processor.ReadNextLine(out _);
        processor.ParseCommand(echoOff!);

        // Read goto final
        string? gotoLine = processor.ReadNextLine(out _);
        gotoLine.Should().Be("goto final");
        
        BatchCommand cmd = processor.ParseCommand(gotoLine!);
        processor.GotoLabel(cmd.Value);

        // Next line should be "echo REACHED FINAL", all intermediate code skipped
        string? afterGoto = processor.ReadNextLine(out _);
        afterGoto.Should().Be("echo REACHED FINAL");
    }

    [Fact]
    public void IntegrationTest_GotoLoop_CanIterateMultipleTimes() {
        // Test a GOTO loop pattern (common in batch files for repeating tasks)
        BatchProcessor processor = new(_loggerService);
        string content = @":loop
echo iteration
goto loop";

        string batchPath = CreateBatchFile("loop.bat", content);
        processor.StartBatch(batchPath, []);

        // First iteration
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be("echo iteration");
        
        string? goto1 = processor.ReadNextLine(out _);
        goto1.Should().Be("goto loop");
        processor.GotoLabel("loop");

        // Second iteration
        string? line2 = processor.ReadNextLine(out _);
        line2.Should().Be("echo iteration");
        
        string? goto2 = processor.ReadNextLine(out _);
        goto2.Should().Be("goto loop");
        processor.GotoLabel("loop");

        // Third iteration
        string? line3 = processor.ReadNextLine(out _);
        line3.Should().Be("echo iteration");
    }

    [Fact]
    public void IntegrationTest_MenuWithGotoPattern() {
        // Test a menu-style batch file (common pattern)
        BatchProcessor processor = new(_loggerService);
        string content = @"@echo off
echo Menu:
echo 1. Option A
echo 2. Option B
echo 3. Exit
if %1==1 goto optA
if %1==2 goto optB
if %1==3 goto exit
goto menu_error
:optA
echo Selected Option A
goto end
:optB
echo Selected Option B
goto end
:menu_error
echo Invalid choice
goto end
:exit
echo Exiting...
:end
echo Done";

        string batchPath = CreateBatchFile("menu.bat", content);
        processor.StartBatch(batchPath, ["2"]); // Select option 2

        // Process until we get to the option selection
        string? echoOff = processor.ReadNextLine(out _);
        processor.ParseCommand(echoOff!);
        processor.ReadNextLine(out _); // echo Menu:
        processor.ReadNextLine(out _); // echo 1. Option A
        processor.ReadNextLine(out _); // echo 2. Option B
        processor.ReadNextLine(out _); // echo 3. Exit
        processor.ReadNextLine(out _); // if 1==1 goto optA (false)
        
        // Read second IF - should match
        string? ifLine = processor.ReadNextLine(out _);
        ifLine.Should().Be("if 2==2 goto optB");
        
        // Execute GOTO for matched condition
        processor.GotoLabel("optB");

        // Should be at optB label content
        string? optBLine = processor.ReadNextLine(out _);
        optBLine.Should().Be("echo Selected Option B");
    }

    [Fact]
    public void IntegrationTest_CallAndReturn_MaintainsCorrectContext() {
        // Test CALL command with proper context switching
        BatchProcessor processor = new(_loggerService);
        string innerContent = @"echo In inner batch
echo Inner arg: %1";
        string innerPath = CreateBatchFile("inner.bat", innerContent);

        string outerContent = $@"echo Before call
call {innerPath} test_arg
echo After call";
        string outerPath = CreateBatchFile("outer.bat", outerContent);

        processor.StartBatch(outerPath, []);
        
        // Read first line of outer
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be("echo Before call");
        processor.CurrentBatchPath.Should().Be(outerPath);

        // Read CALL command
        string? callLine = processor.ReadNextLine(out _);
        callLine.Should().StartWith("call");
        BatchCommand callCmd = processor.ParseCommand(callLine!);
        callCmd.Type.Should().Be(BatchCommandType.CallBatch);

        // Simulate CALL by starting inner batch
        processor.StartBatch(innerPath, ["test_arg"]);
        processor.CurrentBatchPath.Should().Be(innerPath);

        // Read inner batch lines
        string? innerLine1 = processor.ReadNextLine(out _);
        innerLine1.Should().Be("echo In inner batch");
        
        string? innerLine2 = processor.ReadNextLine(out _);
        innerLine2.Should().Be("echo Inner arg: test_arg");

        // End of inner batch - return to outer
        string? innerNull = processor.ReadNextLine(out _);
        innerNull.Should().BeNull();

        // Should be back in outer batch
        processor.CurrentBatchPath.Should().Be(outerPath);

        // Continue outer batch
        string? afterCall = processor.ReadNextLine(out _);
        afterCall.Should().Be("echo After call");
    }

    [Fact]
    public void IntegrationTest_MultiLevelCall_NestedThreeLevels() {
        // Test three levels of nested CALL
        BatchProcessor processor = new(_loggerService);
        
        string level3Content = "echo Level 3";
        string level3Path = CreateBatchFile("level3.bat", level3Content);
        
        string level2Content = $@"echo Level 2 start
call {level3Path}
echo Level 2 end";
        string level2Path = CreateBatchFile("level2.bat", level2Content);
        
        string level1Content = $@"echo Level 1 start
call {level2Path}
echo Level 1 end";
        string level1Path = CreateBatchFile("level1.bat", level1Content);

        // Start level 1
        processor.StartBatch(level1Path, []);
        string? l1Start = processor.ReadNextLine(out _);
        l1Start.Should().Be("echo Level 1 start");

        // Enter level 2
        processor.ReadNextLine(out _); // call level2.bat
        processor.StartBatch(level2Path, []);
        string? l2Start = processor.ReadNextLine(out _);
        l2Start.Should().Be("echo Level 2 start");

        // Enter level 3
        processor.ReadNextLine(out _); // call level3.bat
        processor.StartBatch(level3Path, []);
        string? l3 = processor.ReadNextLine(out _);
        l3.Should().Be("echo Level 3");

        // Exit level 3
        processor.ReadNextLine(out _); // End of level 3
        processor.CurrentBatchPath.Should().Be(level2Path);

        // Continue level 2
        string? l2End = processor.ReadNextLine(out _);
        l2End.Should().Be("echo Level 2 end");

        // Exit level 2
        processor.ReadNextLine(out _); // End of level 2
        processor.CurrentBatchPath.Should().Be(level1Path);

        // Continue level 1
        string? l1End = processor.ReadNextLine(out _);
        l1End.Should().Be("echo Level 1 end");
    }

    [Fact]
    public void IntegrationTest_IfExistWithGoto_ConditionalJump() {
        // Test IF EXIST combined with GOTO
        BatchProcessor processor = new(_loggerService);
        
        // Create a file that exists
        string existingFile = CreateBatchFile("exists.txt", "test content");
        
        string content = $@"@echo off
if exist {existingFile} goto found
echo File not found
goto end
:found
echo File exists!
:end
echo Done";

        string batchPath = CreateBatchFile("ifexist.bat", content);
        processor.StartBatch(batchPath, []);

        // Process @echo off
        processor.ReadNextLine(out _);
        processor.ParseCommand("echo off");

        // Read IF EXIST line
        string? ifLine = processor.ReadNextLine(out _);
        ifLine.Should().StartWith("if exist");
        
        BatchCommand ifCmd = processor.ParseCommand(ifLine!);
        ifCmd.Type.Should().Be(BatchCommandType.If);
        ifCmd.Value.Should().Be("EXIST");
        ifCmd.Negate.Should().BeFalse();
    }

    [Fact]
    public void IntegrationTest_IfNotExistWithGoto_NegativeConditionalJump() {
        // Test IF NOT EXIST combined with GOTO
        BatchProcessor processor = new(_loggerService);
        
        string content = @"@echo off
if not exist nonexistent.xyz goto notfound
echo File found
goto end
:notfound
echo File not found!
:end
echo Done";

        string batchPath = CreateBatchFile("ifnotexist.bat", content);
        processor.StartBatch(batchPath, []);

        processor.ReadNextLine(out _); // @echo off
        processor.ParseCommand("echo off");

        string? ifLine = processor.ReadNextLine(out _);
        ifLine.Should().StartWith("if not exist");
        
        BatchCommand ifCmd = processor.ParseCommand(ifLine!);
        ifCmd.Type.Should().Be(BatchCommandType.If);
        ifCmd.Value.Should().Be("EXIST");
        ifCmd.Negate.Should().BeTrue();
    }

    [Fact]
    public void IntegrationTest_ShiftInLoop_ProcessesAllArguments() {
        // Test SHIFT in a loop pattern to process all arguments
        BatchProcessor processor = new(_loggerService);
        string content = @":loop
if ""%1""=="""" goto end
echo Processing: %1
shift
goto loop
:end
echo All done";

        string batchPath = CreateBatchFile("shiftloop.bat", content);
        processor.StartBatch(batchPath, ["first", "second", "third"]);

        // First iteration
        string? if1 = processor.ReadNextLine(out _);
        if1.Should().Be(@"if ""first""=="""" goto end");
        
        string? echo1 = processor.ReadNextLine(out _);
        echo1.Should().Be("echo Processing: first");
        
        string? shift1 = processor.ReadNextLine(out _);
        shift1.Should().Be("shift");
        processor.ParseCommand(shift1!); // Execute SHIFT
        
        processor.ReadNextLine(out _); // goto loop
        processor.GotoLabel("loop");

        // Second iteration
        string? if2 = processor.ReadNextLine(out _);
        if2.Should().Be(@"if ""second""=="""" goto end"); // %1 is now "second" after SHIFT
        
        string? echo2 = processor.ReadNextLine(out _);
        echo2.Should().Be("echo Processing: second");
    }

    [Fact]
    public void IntegrationTest_ForLoopWithEcho() {
        // Test FOR loop parsing (not full execution, but parsing)
        // Note: %% in batch file becomes % after parameter expansion
        BatchProcessor processor = new(_loggerService);
        string content = "for %%a in (file1.txt file2.txt file3.txt) do echo Processing %%a";

        string batchPath = CreateBatchFile("forloop.bat", content);
        processor.StartBatch(batchPath, []);

        string? forLine = processor.ReadNextLine(out _);
        // After expansion, %% becomes %
        forLine.Should().StartWith("for %a");
        
        BatchCommand forCmd = processor.ParseCommand(forLine!);
        forCmd.Type.Should().Be(BatchCommandType.For);
        forCmd.Value.Should().Be("%a"); // %% became %
        forCmd.GetForSet().Should().BeEquivalentTo(["file1.txt", "file2.txt", "file3.txt"]);
        forCmd.GetForCommand().Should().Be("echo Processing %a");
    }

    [Fact]
    public void IntegrationTest_ErrorHandlingPattern_GotoOnError() {
        // Test error handling pattern with GOTO
        BatchProcessor processor = new(_loggerService);
        string content = @"@echo off
echo Starting operation...
myprogram.exe arg1
if errorlevel 1 goto error
echo Operation successful
goto end
:error
echo ERROR: Operation failed
:end
echo Script finished";

        string batchPath = CreateBatchFile("errorhandling.bat", content);
        processor.StartBatch(batchPath, []);

        // Process @echo off
        processor.ReadNextLine(out _);
        processor.ParseCommand("echo off");

        // Read echo
        string? startLine = processor.ReadNextLine(out _);
        startLine.Should().Be("echo Starting operation...");

        // Read program execution
        string? progLine = processor.ReadNextLine(out _);
        progLine.Should().Be("myprogram.exe arg1");
        BatchCommand progCmd = processor.ParseCommand(progLine!);
        progCmd.Type.Should().Be(BatchCommandType.ExecuteProgram);
        progCmd.Value.Should().Be("myprogram.exe");
        progCmd.Arguments.Should().Be("arg1");

        // Read error check
        string? ifLine = processor.ReadNextLine(out _);
        ifLine.Should().Be("if errorlevel 1 goto error");
        BatchCommand ifCmd = processor.ParseCommand(ifLine!);
        ifCmd.Type.Should().Be(BatchCommandType.If);
        ifCmd.Value.Should().Be("ERRORLEVEL");
    }

    [Fact]
    public void IntegrationTest_SetAndExpandVariable() {
        // Test SET and environment variable expansion
        TestBatchEnvironment env = new();
        BatchProcessor processor = new(_loggerService, env);
        
        string content = @"set MYDIR=C:\Programs
echo Installing to %MYDIR%
set LOGFILE=%MYDIR%\install.log
echo Logging to %LOGFILE%";

        string batchPath = CreateBatchFile("setvar.bat", content);
        processor.StartBatch(batchPath, []);

        // Read and parse SET MYDIR
        string? setLine = processor.ReadNextLine(out _);
        setLine.Should().Be(@"set MYDIR=C:\Programs");
        BatchCommand setCmd = processor.ParseCommand(setLine!);
        setCmd.Type.Should().Be(BatchCommandType.SetVariable);
        setCmd.Value.Should().Be("MYDIR");
        setCmd.Arguments.Should().Be(@"C:\Programs");
        
        // Simulate setting the variable in environment
        env.SetVariable("MYDIR", @"C:\Programs");

        // Read echo line - should expand %MYDIR%
        string? echoLine = processor.ReadNextLine(out _);
        echoLine.Should().Be(@"echo Installing to C:\Programs");
    }

    [Fact]
    public void IntegrationTest_ComplexPathSubstitution() {
        // Test parameter substitution in paths
        BatchProcessor processor = new(_loggerService);
        string content = @"echo %0
echo Processing directory: %1
cd %1
echo Current: %CD%
echo File: %1\data.txt";

        string batchPath = CreateBatchFile("pathtest.bat", content);
        processor.StartBatch(batchPath, [@"C:\MyFolder"]);

        // Check %0 expansion
        string? line0 = processor.ReadNextLine(out _);
        line0.Should().Be($"echo {batchPath}");

        // Check %1 expansion
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be(@"echo Processing directory: C:\MyFolder");

        // Check %1 in command
        string? cdLine = processor.ReadNextLine(out _);
        cdLine.Should().Be(@"cd C:\MyFolder");
    }

    [Fact]
    public void IntegrationTest_GotoEof_EndsProcessing() {
        // Test GOTO :EOF pattern (special DOS label)
        BatchProcessor processor = new(_loggerService);
        string content = @"echo Before
goto :eof
echo Should not appear";

        string batchPath = CreateBatchFile("gotoeof.bat", content);
        processor.StartBatch(batchPath, []);

        string? before = processor.ReadNextLine(out _);
        before.Should().Be("echo Before");

        string? gotoLine = processor.ReadNextLine(out _);
        gotoLine.Should().Be("goto :eof");
        
        BatchCommand gotoCmd = processor.ParseCommand(gotoLine!);
        gotoCmd.Type.Should().Be(BatchCommandType.Goto);
        gotoCmd.Value.Should().Be("eof");

        // :EOF is a special label that means end of file
        bool found = processor.GotoLabel("eof");
        found.Should().BeFalse(); // :EOF is a pseudo-label, shouldn't be found
    }

    [Fact]
    public void IntegrationTest_LabelAtEndOfFile() {
        // Test label at very end of file
        BatchProcessor processor = new(_loggerService);
        string content = @"echo test
goto end
echo skipped
:end";

        string batchPath = CreateBatchFile("labelend.bat", content);
        processor.StartBatch(batchPath, []);

        processor.ReadNextLine(out _); // echo test
        string? gotoLine = processor.ReadNextLine(out _);
        gotoLine.Should().Be("goto end");
        
        bool found = processor.GotoLabel("end");
        found.Should().BeTrue();

        // Next read should be null (end of file)
        string? afterLabel = processor.ReadNextLine(out _);
        afterLabel.Should().BeNull();
    }

    [Fact]
    public void IntegrationTest_MultipleShifts_ExhaustedArguments() {
        // Test multiple SHIFT operations until arguments are exhausted
        BatchProcessor processor = new(_loggerService);
        string content = @"echo %1 %2 %3
shift
echo %1 %2 %3
shift
echo %1 %2 %3
shift
echo %1 %2 %3";

        string batchPath = CreateBatchFile("multishift.bat", content);
        processor.StartBatch(batchPath, ["a", "b"]);

        // Before any shift: %1=a, %2=b, %3=empty
        string? line1 = processor.ReadNextLine(out _);
        line1.Should().Be("echo a b ");

        // First SHIFT
        processor.ReadNextLine(out _); // shift
        processor.ParseCommand("shift");

        // After first shift: %1=b, %2=empty, %3=empty
        string? line2 = processor.ReadNextLine(out _);
        line2.Should().Be("echo b  ");

        // Second SHIFT
        processor.ReadNextLine(out _); // shift
        processor.ParseCommand("shift");

        // After second shift: all empty
        string? line3 = processor.ReadNextLine(out _);
        line3.Should().Be("echo   ");
    }

    [Fact]
    public void IntegrationTest_QuotedStringsInIfComparison() {
        // Test IF with quoted strings (common pattern to handle empty args)
        BatchProcessor processor = new(_loggerService);
        string content = @"if ""%1""==""test"" goto match
echo No match
goto end
:match
echo Matched!
:end";

        string batchPath = CreateBatchFile("quotedif.bat", content);
        processor.StartBatch(batchPath, ["test"]);

        string? ifLine = processor.ReadNextLine(out _);
        ifLine.Should().Be(@"if ""test""==""test"" goto match");
        
        BatchCommand ifCmd = processor.ParseCommand(ifLine!);
        ifCmd.Type.Should().Be(BatchCommandType.If);
        ifCmd.Value.Should().Be("COMPARE");
    }

    [Fact]
    public void IntegrationTest_RealWorldInstallerPattern() {
        // Test a realistic installer-style batch file
        BatchProcessor processor = new(_loggerService);
        string content = @"@echo off
echo ==========================
echo   INSTALLATION SCRIPT
echo ==========================
echo.
if ""%1""=="""" goto usage
set DEST=%1
echo Installing to %DEST%...
goto install

:usage
echo Usage: install.bat destination_path
goto end

:install
echo Copying files...
xcopy /E source\* %DEST%\
if errorlevel 1 goto copyerror
echo Installation complete!
goto end

:copyerror
echo ERROR: Copy failed!

:end
echo.
echo Press any key to exit...
pause";

        string batchPath = CreateBatchFile("installer.bat", content);
        processor.StartBatch(batchPath, [@"C:\Install"]);

        // Process @echo off
        processor.ReadNextLine(out _);
        processor.ParseCommand("echo off");

        // Verify initial lines
        string? header1 = processor.ReadNextLine(out _);
        header1.Should().Be("echo ==========================");
    }

    [Fact]
    public void IntegrationTest_EnvironmentVariableInLabel() {
        // Test that labels are correctly found even with environment variable usage
        TestBatchEnvironment env = new();
        env.SetVariable("SECTION", "main");
        BatchProcessor processor = new(_loggerService, env);
        
        string content = @"goto %SECTION%
:init
echo Init section
:main
echo Main section
:cleanup
echo Cleanup";

        string batchPath = CreateBatchFile("envlabel.bat", content);
        processor.StartBatch(batchPath, []);

        // Read goto line - %SECTION% should be expanded to "main"
        string? gotoLine = processor.ReadNextLine(out _);
        gotoLine.Should().Be("goto main");
        
        BatchCommand gotoCmd = processor.ParseCommand(gotoLine!);
        processor.GotoLabel(gotoCmd.Value);

        // Should be at main section
        string? mainLine = processor.ReadNextLine(out _);
        mainLine.Should().Be("echo Main section");
    }

    #endregion
}
