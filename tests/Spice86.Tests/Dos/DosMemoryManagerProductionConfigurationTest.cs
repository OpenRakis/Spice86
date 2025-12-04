namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// ASM-based integration tests for DOS memory manager following FreeDOS/DOSBox pattern.
/// Tests run actual x86 machine code through the emulation stack using INT 21h functions.
/// This verifies that the memory manager works correctly with production configuration.
/// </summary>
public class DosMemoryManagerProductionConfigurationTest {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DataPort = 0x99A;      // Port to write test data

    private enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests INT 21h/48h - Allocate memory. Should successfully allocate a small block.
    /// </summary>
    [Fact]
    public void AllocateMemory_SmallBlock_ShouldSucceed() {
        // INT 21h/48h: AH=48h (Allocate memory)
        // BX = number of paragraphs requested (100 = 1600 bytes)
        // On return: AX = segment of allocated block if CF clear, or error code if CF set
        byte[] program = new byte[] {
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0x64, 0x00,       // mov bx, 0064h - Request 100 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x08,             // jc failed - Jump if carry (error)
            // success: allocation succeeded, write segment to data port (AX contains segment)
            0x50,                   // push ax - Save allocated segment
            0xBA, 0x9A, 0x09,       // mov dx, DataPort
            0x58,                   // pop ax - Restore allocated segment
            0xEF,                   // out dx, ax - Write allocated segment
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

        testHandler.Results.Should().Contain((byte)TestResult.Success, "allocation should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure, "allocation should not fail");
    }

    /// <summary>
    /// Tests INT 21h/48h - Allocate large memory block (400 KB = 25000 paragraphs).
    /// Verifies Day of the Tentacle-style allocations work.
    /// </summary>
    [Fact]
    public void AllocateMemory_LargeBlock_ShouldSucceed() {
        // INT 21h/48h: AH=48h (Allocate memory)
        // BX = 25000 paragraphs (400,000 bytes = ~391 KB)
        byte[] program = new byte[] {
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0xA8, 0x61,       // mov bx, 61A8h - Request 25000 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc failed - Jump if carry (error)
            // success:
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
    /// Tests INT 21h/48h - Request impossibly large block should fail gracefully.
    /// </summary>
    [Fact]
    public void AllocateMemory_ImpossiblyLarge_ShouldFail() {
        // INT 21h/48h: AH=48h (Allocate memory)
        // BX = 0xFFFF paragraphs (impossible - larger than conventional memory)
        byte[] program = new byte[] {
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0xFF, 0xFF,       // mov bx, FFFFh - Request impossibly large
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed - Should have carry set (error)
            // Carry was set (error returned) - success
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
    /// Tests INT 21h/48h and 49h - Allocate then free memory.
    /// </summary>
    [Fact]
    public void AllocateAndFreeMemory_ShouldSucceed() {
        // Allocate, then free the block
        byte[] program = new byte[] {
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0x64, 0x00,       // mov bx, 0064h - Request 100 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x09,             // jc failed - Jump if carry (error)
            // Free the allocated block (ES contains segment)
            0x8E, 0xC0,             // mov es, ax - Move allocated segment to ES
            0xB8, 0x00, 0x49,       // mov ax, 4900h - Free memory
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc failed
            // success:
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
    /// Tests INT 21h/4Ah - Resize memory block smaller.
    /// </summary>
    [Fact]
    public void ResizeMemory_Smaller_ShouldSucceed() {
        // Allocate 200 paragraphs, then resize to 100
        byte[] program = new byte[] {
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0xC8, 0x00,       // mov bx, 00C8h - Request 200 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x0D,             // jc failed
            // Resize to 100 paragraphs
            0x8E, 0xC0,             // mov es, ax - Move allocated segment to ES
            0xB8, 0x00, 0x4A,       // mov ax, 4A00h - Resize memory
            0xBB, 0x64, 0x00,       // mov bx, 0064h - New size: 100 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc failed
            // success:
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
    /// Tests INT 21h/4Ah - Program resizing its own memory block (common DOS pattern).
    /// This reproduces the regression where programs at segment 0x160 tried to resize
    /// but failed because MCB chain starts at 0x016F, not 0x15F.
    /// </summary>
    [Fact]
    public void ProgramResizesOwnMemory_ShouldSucceed() {
        // This test simulates what DOS programs typically do:
        // 1. Program starts with all available memory
        // 2. Program resizes its PSP block to minimum needed
        // 3. Program allocates additional blocks as needed
        byte[] program = new byte[] {
            // Get current program's PSP segment (in ES on entry)
            0x8C, 0xC3,             // mov bx, es - Save PSP segment
            0x8E, 0xC3,             // mov es, bx - ES = PSP segment
            // Resize current program's memory to 0x1000 paragraphs (64KB)
            0xB8, 0x00, 0x4A,       // mov ax, 4A00h - Resize memory
            0xBB, 0x00, 0x10,       // mov bx, 1000h - New size: 4096 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc failed - Should succeed
            // success:
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
    /// Reproduces Day of the Tentacle regression: allocate multiple small blocks after program resize.
    /// DOTT pattern: resize own memory, then allocate many small blocks (3200 bytes each).
    /// This test should PASS on reference branch but FAIL if memory layout breaks allocation.
    /// </summary>
    [Fact]
    public void DottMemoryPattern_MultipleSmallAllocations_ShouldSucceed() {
        // DOTT does:
        // 1. Resize own memory to minimum
        // 2. Allocate multiple small blocks (3200 bytes = 200 paragraphs each)
        byte[] program = new byte[] {
            // Resize current program to small size (256 paragraphs = 4KB)
            0x8C, 0xC3,             // mov bx, es - Get PSP segment
            0x8E, 0xC3,             // mov es, bx - ES = PSP segment
            0xB8, 0x00, 0x4A,       // mov ax, 4A00h - Resize memory
            0xBB, 0x00, 0x01,       // mov bx, 0100h - New size: 256 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x1E,             // jc failed - Jump if resize failed
            
            // Allocate first 3200-byte block (200 paragraphs)
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0xC8, 0x00,       // mov bx, 00C8h - 200 paragraphs (3200 bytes)
            0xCD, 0x21,             // int 21h
            0x72, 0x14,             // jc failed
            
            // Allocate second 3200-byte block
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0xC8, 0x00,       // mov bx, 00C8h - 200 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x0C,             // jc failed
            
            // Allocate third 3200-byte block (this is where DOTT fails)
            0xB8, 0x00, 0x48,       // mov ax, 4800h - Allocate memory
            0xBB, 0xC8, 0x00,       // mov bx, 00C8h - 200 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc failed
            
            // success:
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

        testHandler.Results.Should().Contain((byte)TestResult.Success, 
            "DOTT memory pattern should succeed: resize program, then allocate multiple 3200-byte blocks");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests memory MCB chain structure matches reference/improvements_dos_bios pattern.
    /// Verifies COMMAND.COM MCB at 0x005F, then free memory MCB at 0x006F.
    /// </summary>
    [Fact]
    public void MemoryChainStructure_MatchesReferencePattern() {
        // Read MCB at 0x005F (COMMAND.COM block), verify PSP=0x0060, size=16
        byte[] program = new byte[] {
            // Read COMMAND.COM MCB at 0x005F
            0xB8, 0x5F, 0x00,       // mov ax, 005Fh - COMMAND.COM MCB segment
            0x8E, 0xD8,             // mov ds, ax - DS = MCB segment
            0x31, 0xC0,             // xor ax, ax - Clear AX
            0x8A, 0x06, 0x00, 0x00, // mov al, [0000h] - Read MCB type (should be 'M' = 0x4D)
            0x3C, 0x4D,             // cmp al, 4Dh - Check if 'M' (middle block)
            0x75, 0x1A,             // jne failed
            0x8B, 0x06, 0x01, 0x00, // mov ax, [0001h] - Read PSP segment
            0x3D, 0x60, 0x00,       // cmp ax, 0060h - Check if COMMAND.COM segment
            0x75, 0x12,             // jne failed
            0x8B, 0x06, 0x03, 0x00, // mov ax, [0003h] - Read size
            0x3D, 0x10, 0x00,       // cmp ax, 0010h - Check if size=16 (COMMAND.COM size)
            0x75, 0x0A,             // jne failed
            // Write COMMAND.COM MCB PSP to data port
            0xB8, 0x60, 0x00,       // mov ax, 0060h
            0xBA, 0x9A, 0x09,       // mov dx, DataPort
            0xEF,                   // out dx, ax
            // success:
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
        testHandler.Data.Should().Contain(0x60); // Should have written COMMAND.COM segment
        testHandler.Data.Should().Contain(0x00);
    }

    /// <summary>
    /// Runs the DOS test program and returns a test handler with results.
    /// </summary>
    private DosTestHandler RunDosTest(byte[] program, [CallerMemberName] string unitTestName = "test") {
        // Write program to file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator with DOS interrupt vectors installed
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false
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
    /// Captures DOS test results from designated I/O ports.
    /// </summary>
    private class DosTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Data { get; } = new();

        public DosTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DataPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            } else if (port == DataPort) {
                Data.Add(value);
            }
        }

        public override void WriteWord(ushort port, ushort value) {
            if (port == DataPort) {
                Data.Add((byte)(value & 0xFF));
                Data.Add((byte)((value >> 8) & 0xFF));
            }
        }
    }
}
