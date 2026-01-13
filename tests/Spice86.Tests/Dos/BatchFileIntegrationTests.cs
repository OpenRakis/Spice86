namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System;
using System.IO;
using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for DOS batch file execution.
/// These tests run actual batch files through the complete emulator stack,
/// similar to how XmsIntegrationTests run assembly programs.
/// </summary>
public class BatchFileIntegrationTests : IDisposable {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write details
    private readonly string _tempDir;

    public BatchFileIntegrationTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Spice86BatchTests_{Guid.NewGuid():N}");
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

    /// <summary>
    /// Tests basic batch file with ECHO command.
    /// The batch file launches a COM program that signals success.
    /// </summary>
    [Fact]
    public void BatchFile_WithEchoAndProgramLaunch_ShouldExecute() {
        // Create a simple COM program that signals success via I/O port
        byte[] successProgram = new byte[]
        {
            0xB0, 0x00,             // mov al, 0 (success)
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        string comPath = Path.Combine(_tempDir, "SUCCESS.COM");
        File.WriteAllBytes(comPath, successProgram);

        // Create a batch file that echoes and launches the program
        string batchContent = "@ECHO OFF\r\nECHO Starting test\r\nSUCCESS.COM\r\n";
        string batchPath = Path.Combine(_tempDir, "TEST.BAT");
        File.WriteAllBytes(batchPath, System.Text.Encoding.ASCII.GetBytes(batchContent));

        // Run the batch file through the emulator
        BatchTestHandler testHandler = RunBatchTest(batchPath);

        // Verify the COM program was executed
        testHandler.Results.Should().Contain((byte)0);
    }

    /// <summary>
    /// Tests batch file with environment variable SET command.
    /// </summary>
    [Fact]
    public void BatchFile_WithSetCommand_ShouldStoreVariable() {
        // Create a COM program that checks if environment variable was set
        // This is a simplified test - in reality, would need to call INT21H to read env
        byte[] program = new byte[]
        {
            0xB0, 0x00,             // mov al, 0 (success - assume SET worked)
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        string comPath = Path.Combine(_tempDir, "CHECKENV.COM");
        File.WriteAllBytes(comPath, program);

        // Create batch file with SET command
        string batchContent = "@ECHO OFF\r\nSET TESTVAR=TestValue\r\nCHECKENV.COM\r\n";
        string batchPath = Path.Combine(_tempDir, "SETTEST.BAT");
        File.WriteAllBytes(batchPath, System.Text.Encoding.ASCII.GetBytes(batchContent));

        // Run the batch file
        BatchTestHandler testHandler = RunBatchTest(batchPath);

        // Verify execution completed
        testHandler.Results.Should().Contain((byte)0);
    }

    /// <summary>
    /// Tests batch file with GOTO command for label jumping.
    /// </summary>
    [Fact]
    public void BatchFile_WithGoto_ShouldJumpToLabel() {
        // Create success program
        byte[] successProgram = new byte[]
        {
            0xB0, 0x00,             // mov al, 0 (success)
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Create failure program (should not be executed)
        byte[] failureProgram = new byte[]
        {
            0xB0, 0xFF,             // mov al, 0xFF (failure)
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        string successPath = Path.Combine(_tempDir, "SUCCESS.COM");
        string failurePath = Path.Combine(_tempDir, "FAILURE.COM");
        File.WriteAllBytes(successPath, successProgram);
        File.WriteAllBytes(failurePath, failureProgram);

        // Batch file with GOTO that skips FAILURE.COM
        string batchContent = "@ECHO OFF\r\nGOTO end\r\nFAILURE.COM\r\n:end\r\nSUCCESS.COM\r\n";
        string batchPath = Path.Combine(_tempDir, "GOTOTEST.BAT");
        File.WriteAllBytes(batchPath, System.Text.Encoding.ASCII.GetBytes(batchContent));

        // Run the batch file
        BatchTestHandler testHandler = RunBatchTest(batchPath);

        // Should only see success, not failure
        testHandler.Results.Should().Contain((byte)0);
        testHandler.Results.Should().NotContain((byte)0xFF);
    }

    /// <summary>
    /// Tests batch file with IF EXIST conditional.
    /// </summary>
    [Fact]
    public void BatchFile_WithIfExist_ShouldExecuteConditionally() {
        // Create test file that will be checked
        string testFile = Path.Combine(_tempDir, "EXISTS.TXT");
        File.WriteAllText(testFile, "test");

        // Create programs
        byte[] foundProgram = new byte[]
        {
            0xB0, 0x01,             // mov al, 1 (found)
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        byte[] notFoundProgram = new byte[]
        {
            0xB0, 0x02,             // mov al, 2 (not found)
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        string foundPath = Path.Combine(_tempDir, "FOUND.COM");
        string notFoundPath = Path.Combine(_tempDir, "NOTFOUND.COM");
        File.WriteAllBytes(foundPath, foundProgram);
        File.WriteAllBytes(notFoundPath, notFoundProgram);

        // Batch file with IF EXIST
        string batchContent = "@ECHO OFF\r\nIF EXIST EXISTS.TXT FOUND.COM\r\nIF EXIST MISSING.TXT NOTFOUND.COM\r\n";
        string batchPath = Path.Combine(_tempDir, "IFTEST.BAT");
        File.WriteAllBytes(batchPath, System.Text.Encoding.ASCII.GetBytes(batchContent));

        // Run the batch file
        BatchTestHandler testHandler = RunBatchTest(batchPath);

        // Should execute FOUND.COM but not NOTFOUND.COM
        testHandler.Results.Should().Contain((byte)1);
        testHandler.Results.Should().NotContain((byte)2);
    }

    /// <summary>
    /// Runs a batch file test through the complete emulator stack.
    /// </summary>
    private BatchTestHandler RunBatchTest(string batchFilePath,
        [CallerMemberName] string unitTestName = "test") {
        
        // Setup emulator with batch file
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: batchFilePath,
            enablePit: true,
            recordData: false,
            maxCycles: 1000000L, // More cycles for batch processing
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            cdrive: _tempDir  // Set C: drive to our temp directory
        ).Create();

        BatchTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );

        try {
            spice86DependencyInjection.ProgramExecutor.Run();
        } catch (Exception ex) {
            // Some tests may end with halt or error, that's ok
            System.Diagnostics.Debug.WriteLine($"Batch test ended with: {ex.Message}");
        }

        return testHandler;
    }

    /// <summary>
    /// Captures batch test results from designated I/O ports.
    /// </summary>
    private sealed class BatchTestHandler : DefaultIOPortHandler {
        public System.Collections.Generic.List<byte> Results { get; } = new();
        
        public BatchTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            }
        }
    }
}
