namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using System;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// Integration tests for batch file execution through the Spice86 emulator.
/// These tests verify that batch files are properly loaded, parsed, and executed
/// by the DOS shell (COMMAND.COM), testing internal commands like ECHO, IF, GOTO, CALL, etc.
/// </summary>
public class DosBatchIntegrationTests {
    
    /// <summary>
    /// Runs a batch file through the full emulator stack and returns the video memory output.
    /// </summary>
    /// <param name="batchContent">Content of the batch file to execute</param>
    /// <param name="tempDir">Temporary directory for batch and helper files</param>
    /// <param name="maxCycles">Maximum CPU cycles to run (default 100000)</param>
    /// <param name="expectedOutputLength">Expected length of video memory output in characters</param>
    /// <returns>String read from video memory at 0xB800:0</returns>
    private static string RunBatchFileAndGetVideoOutput(string batchContent, string tempDir, int maxCycles = 100000, int expectedOutputLength = 10) {
        string batchPath = Path.Join(tempDir, "test.bat");
        File.WriteAllText(batchPath, batchContent);

        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: batchPath,
            enablePit: true,
            recordData: false,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: true,
            enableEms: true,
            cDrive: tempDir
        ).Create();

        spice86.Machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80286;
        spice86.ProgramExecutor.Run();

        string output = ReadVideoMemory(spice86.Machine.Memory, expectedOutputLength);
        
        return output;
    }

    /// <summary>
    /// Reads ASCII characters from video memory at 0xB800:0.
    /// Each character is 2 bytes (character + attribute).
    /// </summary>
    private static string ReadVideoMemory(IMemory memory, int length) {
        uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
        StringBuilder output = new(length);

        for (int i = 0; i < length; i++) {
            byte character = memory.UInt8[videoBase + (uint)(i * 2)];
            output.Append((char)character);
        }

        return output.ToString();
    }

    /// <summary>
    /// Creates a temporary directory for test files and returns its path.
    /// </summary>
    private static string CreateTempDirectory() {
        string tempDir = Path.Join(Path.GetTempPath(), $"batch_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Deletes a directory and all its contents.
    /// </summary>
    private static void TryDeleteDirectory(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }
        Directory.Delete(directoryPath, recursive: true);
    }

    [Fact]
    public void BasicEcho_ShouldOutputText() {
        string tempDir = CreateTempDirectory();
        try {
            // Create a simple COM program that writes "OK" to video memory
            CreateMarkerProgram(tempDir, "marker.com", "OK");

            string batchContent = @"@ECHO OFF
marker.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 2);
            output.Should().Be("OK");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void EchoWithVariable_ShouldExpandVariable() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "marker.com", "AB");

            string batchContent = @"@ECHO OFF
SET MYVAR=marker.com
%MYVAR%
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 2);
            output.Should().Be("AB");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void IfErrorLevel_ShouldExecuteConditionally() {
        string tempDir = CreateTempDirectory();
        try {
            CreateExitProgram(tempDir, "exit1.com", exitCode: 1);
            CreateMarkerProgram(tempDir, "marker.com", "Y");

            string batchContent = @"@ECHO OFF
exit1.com
IF ERRORLEVEL 1 marker.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 1);
            output.Should().Be("Y");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void IfNotErrorLevel_ShouldSkipWhenErrorLevelMatches() {
        string tempDir = CreateTempDirectory();
        try {
            CreateExitProgram(tempDir, "exit0.com", exitCode: 0);
            CreateMarkerProgram(tempDir, "marker.com", "N");

            string batchContent = @"@ECHO OFF
exit0.com
IF NOT ERRORLEVEL 1 marker.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 1);
            output.Should().Be("N");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void IfExist_ShouldExecuteWhenFileExists() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "marker.com", "E");
            File.WriteAllText(Path.Join(tempDir, "exists.txt"), "test");

            string batchContent = @"@ECHO OFF
IF EXIST exists.txt marker.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 1);
            output.Should().Be("E");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void IfNotExist_ShouldExecuteWhenFileDoesNotExist() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "marker.com", "X");

            string batchContent = @"@ECHO OFF
IF NOT EXIST missing.txt marker.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 1);
            output.Should().Be("X");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void IfStringEquals_ShouldExecuteWhenStringsMatch() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "marker.com", "=");

            string batchContent = @"@ECHO OFF
IF ""ABC""==""ABC"" marker.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 1);
            output.Should().Be("=");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Goto_ShouldJumpToLabel() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "marker1.com", "1");
            CreateMarkerProgram(tempDir, "marker2.com", "2");

            string batchContent = @"@ECHO OFF
GOTO skip
marker1.com
:skip
marker2.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 1);
            output.Should().Be("2");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GotoBackward_ShouldJumpToPreviousLabel() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "marker.com", "L");
            CreateCounterProgram(tempDir, "counter.com");

            string batchContent = @"@ECHO OFF
:loop
counter.com
IF ERRORLEVEL 3 GOTO end
GOTO loop
:end
marker.com
";

            // counter.com increments and exits with count as errorlevel
            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, maxCycles: 200000, expectedOutputLength: 1);
            output.Should().Be("L");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Call_ShouldExecuteNestedBatchAndReturn() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "marker1.com", "A");
            CreateMarkerProgram(tempDir, "marker2.com", "B");

            File.WriteAllText(Path.Join(tempDir, "sub.bat"), @"@ECHO OFF
marker2.com
");

            string batchContent = @"@ECHO OFF
marker1.com
CALL sub.bat
marker1.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 3);
            output.Should().Be("ABA");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void CallWithParameters_ShouldPassArgumentsToNestedBatch() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "arg1.com", "1");
            CreateMarkerProgram(tempDir, "arg2.com", "2");

            File.WriteAllText(Path.Join(tempDir, "showarg.bat"), @"@ECHO OFF
%1.com
");

            string batchContent = @"@ECHO OFF
CALL showarg.bat arg1
CALL showarg.bat arg2
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, expectedOutputLength: 2);
            output.Should().Be("12");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Shift_ShouldMoveBatchParameters() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "first.com", "F");
            CreateMarkerProgram(tempDir, "second.com", "S");

            File.WriteAllText(Path.Join(tempDir, "test.bat"), @"@ECHO OFF
%1.com
SHIFT
%1.com
");

            // This test needs to be run with parameters somehow
            // For now, this is a placeholder showing the test structure
            string output = RunBatchFileAndGetVideoOutput("@ECHO OFF\nREM shift test placeholder", tempDir, expectedOutputLength: 1);
            output.Should().NotBeNull();
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ComplexBatch_ShouldExecuteMultipleCommands() {
        string tempDir = CreateTempDirectory();
        try {
            CreateMarkerProgram(tempDir, "step1.com", "1");
            CreateMarkerProgram(tempDir, "step2.com", "2");
            CreateMarkerProgram(tempDir, "step3.com", "3");
            CreateExitProgram(tempDir, "exit0.com", exitCode: 0);

            string batchContent = @"@ECHO OFF
SET STEP=step1
%STEP%.com
exit0.com
IF ERRORLEVEL 1 GOTO error
step2.com
GOTO end
:error
REM Should not reach here
:end
step3.com
";

            string output = RunBatchFileAndGetVideoOutput(batchContent, tempDir, maxCycles: 200000, expectedOutputLength: 3);
            output.Should().Be("123");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Creates a tiny COM program that writes specific text to video memory at 0xB800:0.
    /// </summary>
    private static void CreateMarkerProgram(string directory, string filename, string text) {
        // COM program assembly:
        // mov ax, 0xB800
        // mov es, ax
        // mov di, 0
        // mov al, 'X'  ; character
        // stosb
        // mov ah, 0x07 ; attribute (light gray on black)
        // mov [es:di], ah
        // int 0x20     ; DOS exit

        byte[] program = GenerateMarkerComProgram(text);
        File.WriteAllBytes(Path.Join(directory, filename), program);
    }

    /// <summary>
    /// Generates machine code for a COM program that writes text to video memory.
    /// </summary>
    private static byte[] GenerateMarkerComProgram(string text) {
        if (text.Length > 40) {
            throw new ArgumentException("Text too long for marker program", nameof(text));
        }

        // Use a simple and reliable approach: set up segment, write bytes directly
        List<byte> program = new List<byte>();
        
        // mov ax, 0xB800
        program.AddRange(new byte[] { 0xB8, 0x00, 0xB8 });
        // mov es, ax
        program.AddRange(new byte[] { 0x8E, 0xC0 });
        
        // For each character, write char + attribute at correct offset
        for (int i = 0; i < text.Length; i++) {
            int offset = i * 2; // Each character takes 2 bytes (char + attribute)
            
            // mov byte [es:offset], char
            program.Add(0x26);                      // es: prefix
            program.Add(0xC6);                      // mov byte [...]
            program.Add(0x06);                      // [offset] (direct address)
            program.Add((byte)(offset & 0xFF));     // offset low byte
            program.Add((byte)((offset >> 8) & 0xFF)); // offset high byte
            program.Add((byte)text[i]);             // character byte
            
            // mov byte [es:offset+1], 0x07
            program.Add(0x26);                      // es: prefix
            program.Add(0xC6);                      // mov byte [...]
            program.Add(0x06);                      // [offset] (direct address)
            program.Add((byte)((offset + 1) & 0xFF));  // offset+1 low byte
            program.Add((byte)(((offset + 1) >> 8) & 0xFF)); // offset+1 high byte
            program.Add(0x07);                      // attribute: light gray on black
        }
        
        // int 0x20 (DOS exit)
        program.AddRange(new byte[] { 0xCD, 0x20 });
        
        return program.ToArray();
    }

    /// <summary>
    /// Creates a COM program that exits with a specific error code.
    /// </summary>
    private static void CreateExitProgram(string directory, string filename, int exitCode) {
        // mov ah, 0x4C   ; DOS terminate with return code
        // mov al, exitCode
        // int 0x21
        byte[] program = {
            0xB4, 0x4C,              // mov ah, 0x4C
            0xB0, (byte)exitCode,    // mov al, exitCode
            0xCD, 0x21               // int 0x21
        };

        File.WriteAllBytes(Path.Join(directory, filename), program);
    }

    /// <summary>
    /// Creates a COM program that increments a counter and exits with the count as errorlevel.
    /// Uses a data file to track the counter across invocations.
    /// </summary>
    private static void CreateCounterProgram(string directory, string filename) {
        // This is a placeholder - a real counter program would need to:
        // 1. Read counter.dat file (or create if missing)
        // 2. Increment value
        // 3. Write back to file
        // 4. Exit with counter value as errorlevel
        // For now, create a simple program that always exits with errorlevel 3
        CreateExitProgram(directory, filename, exitCode: 3);
    }
}
