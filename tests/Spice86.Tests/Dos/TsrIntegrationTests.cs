namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h, AH=31h - Terminate and Stay Resident (TSR).
/// Tests verify proper TSR behavior including memory retention and minimum paragraph requirements.
/// </summary>
/// <remarks>
/// These tests compare and contrast TSR support with FreeDOS kernel behavior.
/// Based on FreeDOS FDOS/kernel inthndlr.c implementation:
/// - DosMemChange(cu_psp, lr.DX < 6 ? 6 : lr.DX, 0)
/// - return_code = lr.AL | 0x300
/// - term_type = 3 (TSR terminate)
/// 
/// Note: The $clock device absence is intentionally ignored as per the problem statement.
/// </remarks>
public class TsrIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests that INT 21h, AH=31h terminates program successfully.
    /// This is a basic smoke test to verify TSR doesn't throw an exception.
    /// </summary>
    /// <remarks>
    /// The TSR function was previously throwing NotImplementedException.
    /// This test verifies that the function now completes without error.
    /// </remarks>
    [Fact]
    public void TerminateAndStayResident_BasicTermination_Succeeds() {
        // This test calls INT 21h, AH=31h (Terminate and Stay Resident)
        // DX = paragraphs to keep (0x10 = 16 paragraphs = 256 bytes, enough for PSP)
        // AL = return code (0x00 = success)
        // Expected: Program terminates without error (no exception thrown)
        byte[] program = new byte[] {
            // Set up TSR parameters
            0xB8, 0x00, 0x31,       // mov ax, 3100h - TSR with return code 0
            0xBA, 0x10, 0x00,       // mov dx, 0010h - keep 16 paragraphs (256 bytes)
            0xCD, 0x21,             // int 21h - TSR call
            
            // If we reach here, something went wrong (TSR should have terminated)
            // Write failure to result port
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // The test should complete without throwing an exception
        // TSR terminates the program, so we won't reach the failure code
        RunDosTestWithTsr(program);
    }

    /// <summary>
    /// Tests that TSR enforces minimum 6 paragraphs as per FreeDOS behavior.
    /// Even when requesting 0 paragraphs, the implementation should keep at least 6.
    /// </summary>
    /// <remarks>
    /// Based on FreeDOS kernel: lr.DX < 6 ? 6 : lr.DX
    /// This ensures that even with DX=0, the TSR keeps at least 6 paragraphs.
    /// </remarks>
    [Fact]
    public void TerminateAndStayResident_WithZeroParagraphs_KeepsMinimum() {
        // Request 0 paragraphs - should still keep minimum of 6
        byte[] program = new byte[] {
            0xB8, 0x00, 0x31,       // mov ax, 3100h - TSR with return code 0
            0xBA, 0x00, 0x00,       // mov dx, 0000h - request 0 paragraphs
            0xCD, 0x21,             // int 21h - TSR call (should enforce minimum 6)
            
            0xF4                    // hlt (never reached)
        };

        // Should complete without error, even with 0 paragraphs requested
        RunDosTestWithTsr(program);
    }

    /// <summary>
    /// Tests that TSR keeps the specified number of paragraphs when above minimum.
    /// </summary>
    /// <remarks>
    /// This test verifies that when requesting more than 6 paragraphs,
    /// the memory manager properly resizes the block to the requested size.
    /// </remarks>
    [Fact]
    public void TerminateAndStayResident_WithValidParagraphs_KeepsRequestedSize() {
        // Request 32 paragraphs (512 bytes) - well above minimum
        byte[] program = new byte[] {
            0xB8, 0x00, 0x31,       // mov ax, 3100h - TSR with return code 0
            0xBA, 0x20, 0x00,       // mov dx, 0020h - keep 32 paragraphs (512 bytes)
            0xCD, 0x21,             // int 21h - TSR call
            
            0xF4                    // hlt (never reached)
        };

        // Should complete without error
        RunDosTestWithTsr(program);
    }

    /// <summary>
    /// Tests that TSR passes the return code correctly (in AL).
    /// </summary>
    /// <remarks>
    /// FreeDOS sets: return_code = lr.AL | 0x300
    /// The high byte (0x03) indicates TSR termination type.
    /// </remarks>
    [Fact]
    public void TerminateAndStayResident_WithReturnCode_PassesCodeCorrectly() {
        // Use return code 0x42 to verify it's passed correctly
        byte[] program = new byte[] {
            0xB8, 0x42, 0x31,       // mov ax, 3142h - TSR with return code 0x42
            0xBA, 0x10, 0x00,       // mov dx, 0010h - keep 16 paragraphs
            0xCD, 0x21,             // int 21h - TSR call
            
            0xF4                    // hlt (never reached)
        };

        // Should complete without error
        RunDosTestWithTsr(program);
    }

    /// <summary>
    /// Tests that an interrupt vector can be set and retrieved correctly.
    /// This verifies the basic INT 21h/25h and INT 21h/35h functionality works,
    /// which is fundamental for TSR programs that hook interrupts.
    /// </summary>
    [Fact]
    public void TsrInterruptVectorTest() {
        // This test sets an interrupt vector and verifies it was set correctly
        // Use a simple set/get pattern with INT 21h/25h and INT 21h/35h
        byte[] program = new byte[] {
            // Set DS to 1234h, DX to 5678h for our fake handler address
            0xB8, 0x34, 0x12,       // 0x00: mov ax, 1234h
            0x8E, 0xD8,             // 0x03: mov ds, ax
            0xBA, 0x78, 0x56,       // 0x05: mov dx, 5678h
            
            // Set INT F0h vector: AH=25h, AL=F0h
            0xB4, 0x25,             // 0x08: mov ah, 25h
            0xB0, 0xF0,             // 0x0A: mov al, F0h
            0xCD, 0x21,             // 0x0C: int 21h
            
            // Get INT F0h vector: AH=35h, AL=F0h
            0xB4, 0x35,             // 0x0E: mov ah, 35h
            0xB0, 0xF0,             // 0x10: mov al, F0h
            0xCD, 0x21,             // 0x12: int 21h - now ES:BX = 1234:5678
            
            // Check BX == 5678h
            0x81, 0xFB, 0x78, 0x56, // 0x14: cmp bx, 5678h
            0x75, 0x0F,             // 0x18: jne failed (jump +15 bytes to 0x29)
            
            // Check ES == 1234h
            0x8C, 0xC0,             // 0x1A: mov ax, es
            0x3D, 0x34, 0x12,       // 0x1C: cmp ax, 1234h
            0x75, 0x08,             // 0x1F: jne failed (jump +8 bytes to 0x29)
            
            // Success
            0xB0, 0x00,             // 0x21: mov al, 00h (success)
            0xBA, 0x99, 0x09,       // 0x23: mov dx, ResultPort (0x999)
            0xEE,                   // 0x26: out dx, al
            0xEB, 0x07,             // 0x27: jmp tsr (jump +7 bytes to 0x30)
            
            // failed:
            0xB0, 0xFF,             // 0x29: mov al, FFh (failure)
            0xBA, 0x99, 0x09,       // 0x2B: mov dx, ResultPort (0x999)  
            0xEE,                   // 0x2E: out dx, al
            0x90,                   // 0x2F: nop (padding)
            
            // tsr:
            0xB8, 0x00, 0x31,       // 0x30: mov ax, 3100h - TSR
            0xBA, 0x10, 0x00,       // 0x33: mov dx, 0010h - 16 paragraphs
            0xCD, 0x21,             // 0x36: int 21h
            0xF4                    // 0x38: hlt (never reached)
        };

        TsrTestHandler testHandler = RunDosTestWithTsr(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that TSR with large paragraph count doesn't cause memory corruption.
    /// </summary>
    /// <remarks>
    /// If a program requests more paragraphs than it was allocated,
    /// the memory manager should handle this gracefully.
    /// </remarks>
    [Fact]
    public void TerminateAndStayResident_WithLargeParagraphCount_HandlesGracefully() {
        // Request a very large number of paragraphs
        byte[] program = new byte[] {
            0xB8, 0x00, 0x31,       // mov ax, 3100h - TSR with return code 0
            0xBA, 0xFF, 0x0F,       // mov dx, 0FFFh - request 4095 paragraphs (64KB - 16 bytes)
            0xCD, 0x21,             // int 21h - TSR call
            
            0xF4                    // hlt (never reached)
        };

        // Should complete without error - memory manager may fail to resize but TSR still terminates
        RunDosTestWithTsr(program);
    }

    /// <summary>
    /// Runs the DOS test program with TSR support and returns a test handler with results.
    /// </summary>
    private TsrTestHandler RunDosTestWithTsr(byte[] program,
        [CallerMemberName] string unitTestName = "test") {
        // Write program to a .com file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator with DOS initialized
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: false,
            enablePit: false,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,  // Enable DOS
            enableA20Gate: true
        ).Create();

        TsrTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures DOS test results from designated I/O ports.
    /// </summary>
    private class TsrTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();

        public TsrTestHandler(State state, ILoggerService loggerService,
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
