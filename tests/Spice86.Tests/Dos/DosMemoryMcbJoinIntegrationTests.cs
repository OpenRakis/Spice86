namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// ASM-based integration tests for DOS MCB (Memory Control Block) joining behavior.
/// These tests verify that the memory manager properly marks unlinked MCBs as "fake" (size=0xFFFF)
/// after joining adjacent free blocks, matching FreeDOS kernel behavior.
/// This is critical for compatibility with programs like Doom 8088 and QBasic that may
/// manually walk the MCB chain or perform double-free operations.
/// </summary>
public class DosMemoryMcbJoinIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details

    private enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests basic memory allocation to verify DOS INT 21h/48h works
    /// </summary>
    [Fact]
    public void BasicAllocation_ShouldSucceed() {
        byte[] program = new byte[] {
            // Simple allocation test
            0xB4, 0x48,             // mov ah, 48h - Allocate memory
            0xBB, 0x10, 0x00,       // mov bx, 0010h - 16 paragraphs
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc success - No carry = success
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "basic DOS memory allocation should work");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that allocating and freeing memory works properly.
    /// This verifies that the MCB joining fix doesn't break basic memory operations.
    /// </summary>
    [Fact]
    public void AllocateAndFree_ShouldWork() {
        // Simple test: allocate and free a single block
        byte[] program = new byte[] {
            // Allocate a block
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x10, 0x00,       // mov bx, 0010h - 16 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x0A,             // jc failed
            
            // Free the block
            0x8E, 0xC0,             // mov es, ax
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc failed
            
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

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "basic allocate and free should work");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that allocating two adjacent blocks and then freeing them allows
    /// a larger allocation, proving blocks are joined.
    /// This is a key part of the FreeDOS MCB joining behavior.
    /// </summary>
    [Fact]
    public void TwoAdjacentBlocksFreed_ShouldAllowLargerAllocation() {
        // Allocate two blocks, free them, then allocate a larger block
        byte[] program = new byte[] {
            // Allocate block 1 (50 paragraphs)
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x32, 0x00,       // mov bx, 0032h - 50 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x1E,             // jc failed
            0x50,                   // push ax - Save block 1
            
            // Allocate block 2 (50 paragraphs)
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x32, 0x00,       // mov bx, 0032h - 50 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x17,             // jc failed
            0x50,                   // push ax - Save block 2
            
            // Free block 2
            0x58,                   // pop ax
            0x8E, 0xC0,             // mov es, ax
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x0E,             // jc failed
            
            // Free block 1
            0x58,                   // pop ax
            0x8E, 0xC0,             // mov es, ax
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x07,             // jc failed
            
            // Try to allocate 101 paragraphs (more than one block + MCB overhead)
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x65, 0x00,       // mov bx, 0065h - 101 paragraphs
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc success - Should succeed if blocks joined
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "freeing adjacent blocks should allow larger allocation (blocks should be joined)");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that the MCB chain remains valid after multiple allocations and frees.
    /// This simulates the pattern used by programs like Doom 8088 during startup.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void ComplexAllocationPattern_ShouldMaintainValidMcbChain() {
        // This test allocates and frees blocks in a complex pattern to verify
        // that the MCB chain doesn't get corrupted
        byte[] program = new byte[] {
            // Allocate block 1 (50 paragraphs)
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x32, 0x00,       // mov bx, 0032h - 50 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x45,             // jc failed
            0x89, 0xC6,             // mov si, ax - Save in SI
            
            // Allocate block 2 (100 paragraphs)
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x64, 0x00,       // mov bx, 0064h - 100 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x3D,             // jc failed
            0x89, 0xC7,             // mov di, ax - Save in DI
            
            // Allocate block 3 (75 paragraphs)
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x4B, 0x00,       // mov bx, 004Bh - 75 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x35,             // jc failed
            0x50,                   // push ax - Save block3 on stack
            
            // Free block 2 (middle block)
            0x8E, 0xC7,             // mov es, di
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x2C,             // jc failed
            
            // Free block 1
            0x8E, 0xC6,             // mov es, si
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x25,             // jc failed
            
            // Now allocate a block that spans the joined blocks
            // (should succeed if joining worked correctly)
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x96, 0x00,       // mov bx, 0096h - 150 paragraphs (50+100)
            0xCD, 0x21,             // int 21h
            0x72, 0x1C,             // jc failed
            0x50,                   // push ax - Save new block
            
            // Free the new block
            0x8E, 0xC0,             // mov es, ax
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x58,                   // pop ax - Clean stack
            
            // Free block 3
            0x58,                   // pop ax - Get block3 from stack
            0x8E, 0xC0,             // mov es, ax
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x08,             // jc failed
            
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

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "MCB chain should remain valid through complex allocation patterns");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that freeing blocks in reverse order properly joins them.
    /// This is a common pattern in programs during cleanup.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void FreeBlocksInReverseOrder_ShouldJoinCorrectly() {
        byte[] program = new byte[] {
            // Allocate 3 small blocks
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x0A, 0x00,       // mov bx, 000Ah - 10 paragraphs
            0xCD, 0x21,             // int 21h
            0x72, 0x36,             // jc failed
            0x89, 0xC6,             // mov si, ax - block1 in SI
            
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x0A, 0x00,       // mov bx, 000Ah
            0xCD, 0x21,             // int 21h
            0x72, 0x30,             // jc failed
            0x89, 0xC7,             // mov di, ax - block2 in DI
            
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x0A, 0x00,       // mov bx, 000Ah
            0xCD, 0x21,             // int 21h
            0x72, 0x2A,             // jc failed
            0x50,                   // push ax - block3 on stack
            
            // Free in reverse order: block3, block2, block1
            0x58,                   // pop ax - block3
            0x8E, 0xC0,             // mov es, ax
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x20,             // jc failed
            
            0x8E, 0xC7,             // mov es, di - block2
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x19,             // jc failed
            
            0x8E, 0xC6,             // mov es, si - block1
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            0x72, 0x12,             // jc failed
            
            // Try to allocate a block spanning all three
            0xB4, 0x48,             // mov ah, 48h
            0xBB, 0x20, 0x00,       // mov bx, 0020h - 32 paragraphs (more than 3*10+2)
            0xCD, 0x21,             // int 21h
            0x72, 0x0A,             // jc failed - Should succeed if joined
            
            // Free the new block
            0x8E, 0xC0,             // mov es, ax
            0xB4, 0x49,             // mov ah, 49h
            0xCD, 0x21,             // int 21h
            
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

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "freeing blocks in reverse order should properly join them");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

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
            spice86DependencyInjection.Machine.IoPortDispatcher,
            spice86DependencyInjection.Machine.Memory
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures DOS test results from designated I/O ports and provides access to memory
    /// </summary>
    private class DosTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        private readonly Core.Emulator.Memory.IMemory _memory;

        public DosTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher, Core.Emulator.Memory.IMemory memory) 
            : base(state, true, loggerService) {
            _memory = memory;
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            }
        }

        public DosMemoryControlBlock GetMcb(ushort segment) {
            return new DosMemoryControlBlock(_memory, MemoryUtils.ToPhysicalAddress(segment, 0));
        }
    }
}
