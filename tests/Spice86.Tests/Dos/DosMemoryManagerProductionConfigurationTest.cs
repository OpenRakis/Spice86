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
            0x72, 0x06,             // jc failed - Jump if carry (error)
            // success: allocation succeeded, write segment to data port
            0x8C, 0xC2,             // mov dx, es - ES contains allocated segment
            0xBA, 0x9A, 0x09,       // mov dx, DataPort
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

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
        testHandler.Data.Should().NotBeEmpty("allocated segment should be written");
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
    /// Tests memory MCB chain structure matches FreeDOS/DOSBox pattern.
    /// Verifies device MCB at 0x016F, then environment/locked blocks before user memory.
    /// </summary>
    [Fact]
    public void MemoryChainStructure_MatchesFreeDosPattern() {
        // Read MCB at 0x016F (device block), verify PSP=0x0008, size=1
        byte[] program = new byte[] {
            // Read device MCB at 0x016F
            0xB8, 0x6F, 0x01,       // mov ax, 016Fh - Device MCB segment
            0x8E, 0xD8,             // mov ds, ax - DS = MCB segment
            0x31, 0xC0,             // xor ax, ax - Clear AX
            0x8A, 0x06, 0x00, 0x00, // mov al, [0000h] - Read MCB type (should be 'M' = 0x4D)
            0x3C, 0x4D,             // cmp al, 4Dh - Check if 'M' (middle block)
            0x75, 0x1A,             // jne failed
            0x8B, 0x06, 0x01, 0x00, // mov ax, [0001h] - Read PSP segment
            0x3D, 0x08, 0x00,       // cmp ax, 0008h - Check if MCB_DOS (0x0008)
            0x75, 0x12,             // jne failed
            0x8B, 0x06, 0x03, 0x00, // mov ax, [0003h] - Read size
            0x3D, 0x01, 0x00,       // cmp ax, 0001h - Check if size=1
            0x75, 0x0A,             // jne failed
            // Write device MCB PSP to data port
            0xB8, 0x08, 0x00,       // mov ax, 0008h
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
        testHandler.Data.Should().Contain(0x08); // Should have written MCB_DOS value
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
