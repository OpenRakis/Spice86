namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h functionality that run machine code through the emulation stack.
/// Tests verify DOS structures (CDS, DBCS) are properly initialized and accessible via INT 21h calls.
/// </summary>
public class DosInt21IntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests INT 21h, AH=63h, AL=00h - Get DBCS Lead Byte Table
    /// Verifies that the function returns a valid DS:SI pointer to the DBCS table
    /// </summary>
    [Fact]
    public void GetDbcsLeadByteTable_WithAL0_ReturnsValidPointer() {
        // This test calls INT 21h, AH=63h, AL=00h to get the DBCS table pointer
        // Expected: DS:SI points to DBCS table, AL=0, CF=0
        byte[] program = new byte[] {
            0xB8, 0x00, 0x63,       // mov ax, 6300h - Get DBCS lead byte table (AL=0)
            0xCD, 0x21,             // int 21h
            
            // Check AL = 0 (success)
            0x3C, 0x00,             // cmp al, 0
            0x75, 0x14,             // jne failed (jump to failed if AL != 0)
            
            // Check CF = 0 (no error)
            0x72, 0x11,             // jc failed (jump to failed if carry flag is set)
            
            // Check DS != 0 (valid segment)
            0x8C, 0xDA,             // mov dx, ds
            0x83, 0xFA, 0x00,       // cmp dx, 0
            0x74, 0x0B,             // je failed (jump to failed if DS == 0)
            
            // Check that DS:SI points to a value of 0 (empty DBCS table)
            0x8B, 0x04,             // mov ax, [si]
            0x83, 0xF8, 0x00,       // cmp ax, 0
            0x75, 0x04,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h, AH=63h, AL!=00h - Get DBCS Lead Byte Table with invalid subfunction
    /// Verifies that the function returns error code AL=0xFF for invalid subfunctions
    /// </summary>
    [Fact]
    public void GetDbcsLeadByteTable_WithInvalidAL_ReturnsError() {
        // This test calls INT 21h, AH=63h, AL=01h (invalid subfunction)
        // Expected: AL=0xFF (error)
        byte[] program = new byte[] {
            0xB8, 0x01, 0x63,       // mov ax, 6301h - Invalid subfunction (AL=1)
            0xCD, 0x21,             // int 21h
            
            // Check AL = 0xFF (error)
            0x3C, 0xFF,             // cmp al, 0FFh
            0x75, 0x04,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that CDS (Current Directory Structure) is at the expected memory location
    /// Verifies the structure at segment 0x108 contains "C:\" (0x005c3a43)
    /// </summary>
    [Fact]
    public void CurrentDirectoryStructure_IsInitializedAtKnownLocation() {
        // This test verifies the CDS is at segment 0x108 with "C:\" initialized
        // Based on MemoryMap.DosCdsSegment = 0x108
        byte[] program = new byte[] {
            // Load CDS segment (0x108) into DS
            0xB8, 0x08, 0x01,       // mov ax, 0108h - CDS segment
            0x8E, 0xD8,             // mov ds, ax
            0x31, 0xF6,             // xor si, si - offset 0
            
            // Check first 4 bytes for "C:\" (0x005c3a43 in little-endian)
            // Byte 0: 0x43 ('C')
            0xAC,                   // lodsb (load byte from DS:SI into AL, increment SI)
            0x3C, 0x43,             // cmp al, 43h
            0x75, 0x12,             // jne failed
            
            // Byte 1: 0x3A (':')
            0xAC,                   // lodsb
            0x3C, 0x3A,             // cmp al, 3Ah
            0x75, 0x0D,             // jne failed
            
            // Byte 2: 0x5C ('\')
            0xAC,                   // lodsb
            0x3C, 0x5C,             // cmp al, 5Ch
            0x75, 0x08,             // jne failed
            
            // Byte 3: 0x00 (null terminator)
            0xAC,                   // lodsb
            0x3C, 0x00,             // cmp al, 00h
            0x75, 0x03,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that DBCS table is in DOS private tables area (0xC800-0xD000)
    /// Verifies the table returned by INT 21h AH=63h is in the expected memory range
    /// </summary>
    [Fact]
    public void DbcsTable_IsInPrivateTablesArea() {
        // This test verifies the DBCS table is in the private tables segment range
        byte[] program = new byte[] {
            0xB8, 0x00, 0x63,       // mov ax, 6300h - Get DBCS lead byte table
            0xCD, 0x21,             // int 21h
            
            // DS now contains the segment, check it's >= 0xC800
            0x8C, 0xDA,             // mov dx, ds
            0x81, 0xFA, 0x00, 0xC8, // cmp dx, 0C800h
            0x72, 0x0A,             // jb failed (below C800h)
            
            // Check it's < 0xD000
            0x81, 0xFA, 0x00, 0xD0, // cmp dx, 0D000h
            0x73, 0x04,             // jae failed (>= D000h)
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Runs the DOS test program and returns a test handler with results
    /// </summary>
    private DosTestHandler RunDosTest(byte[] program,
        [CallerMemberName] string unitTestName = "test") {
        // Write program to a .com file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator with DOS initialized
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: false,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,  // Enable DOS
            enableA20Gate: true
        ).Create();

        DosTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures DOS test results from designated I/O ports
    /// </summary>
    private class DosTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();

        public DosTestHandler(State state, ILoggerService loggerService,
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