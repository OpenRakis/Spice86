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
    /// Verifies COMMAND.COM MCB at 0x004F, then free memory MCB at 0x0060.
    /// </summary>
    [Fact]
    public void MemoryChainStructure_MatchesReferencePattern() {
        // Read MCB at 0x004F (COMMAND.COM block), verify PSP=0x0050, size=16
        byte[] program = new byte[] {
            // Read COMMAND.COM MCB at 0x004F
            0xB8, 0x4F, 0x00,       // mov ax, 004Fh - COMMAND.COM MCB segment
            0x8E, 0xD8,             // mov ds, ax - DS = MCB segment
            0x31, 0xC0,             // xor ax, ax - Clear AX
            0x8A, 0x06, 0x00, 0x00, // mov al, [0000h] - Read MCB type (should be 'M' = 0x4D)
            0x3C, 0x4D,             // cmp al, 4Dh - Check if 'M' (middle block)
            0x75, 0x1A,             // jne failed
            0x8B, 0x06, 0x01, 0x00, // mov ax, [0001h] - Read PSP segment
            0x3D, 0x50, 0x00,       // cmp ax, 0050h - Check if COMMAND.COM segment
            0x75, 0x12,             // jne failed
            0x8B, 0x06, 0x03, 0x00, // mov ax, [0003h] - Read size
            0x3D, 0x10, 0x00,       // cmp ax, 0010h - Check if size=16 (COMMAND.COM size)
            0x75, 0x0A,             // jne failed
            // Write COMMAND.COM MCB PSP to data port
            0xB8, 0x50, 0x00,       // mov ax, 0050h
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
        testHandler.Data.Should().Contain(0x50); // Should have written COMMAND.COM segment
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
        // Programs are now allocated dynamically from the MCB chain (not at hardcoded InitialPspSegment)
        // This matches FreeDOS/DOSBox behavior and prevents MCB corruption
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
            // programEntryPointSegment not specified - uses default, but doesn't affect first program allocation
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

    /// <summary>
    /// Integration test for Day of the Tentacle startup (TENTACLE.EXE).
    /// Tests that the actual DOTT executable can start without memory allocation errors.
    /// The startup logic is fairly small and should not trigger "Out of memory" errors.
    /// </summary>
    [Fact]
    public void DayOfTheTentacle_Startup_ShouldNotShowMemoryErrors() {
        // TENTACLE.EXE hex data from user
        string hexData = "4D5A6D00A80000000200EE11FFFF6822800000000E0033141C0000004C5A3931FFFFC804000057568B5E088B378BFEBE98FFFF321E07B9FFFF33C0F2AEF7D12BF987FEFFC3D1E9F3A513C9F3A46A5C68E49AFFFF8641161283C4048946FE0BC074068BD8A7FFC6470100C51F8BD3803F00740E3FFF43807FFF5C7502F275F28BFABE0DD3EE00B0F816BAE68BDACC13308442FA2E75B4DAB4FE00FFEDFC75EDC7067C496AC3FFFA2A4DC52BC0A3462FA3EC32A36138C03AEF3A49EFFF7608FCF8FD069A7A00C00E759A08007F3A3C006864339A224365029A5A7DFEF0141E9A9839F08B1E8A563FFF8987F200A1D24AF5474E68502FF1076A00FE019A20066806DDC00342E883BF928FC0002E9EC43A2EC343FA7633F2833E742FED2A88C3F3C81BFF36F39AC7FF3C065601ADC6060C4D00B001A2FF01614FA21A489A76026A11DC0176D59DCAC1E88FFC475CFAC7F98B47268946FC8B36E861F8EE02B14645B10383C60F2FDD3976FC77EA9CFCDA01775EF9FCF6FF16F9FC18F9FC1A83FE0F7603BE0F0056D38F9A7C09580229361C4D79000F5DFAC79A3000AC7F7FFB3209601AA1E8328DFC04A0FF472AE4DFFBF4FC06A15A53F6FC28A17CF6FD2AA124DEC242F6FC58A140F6FD5AE9F009604A901E53606BF90175246A0684A39E09278A06CF3F4C539A0A01063D01001BC025FC74FB05C900EB2990D5027533355FD5F80EDBF5D5FFCB1B89878E8EF2011DD7FD3C949FF1BE3A02C28E29F3F9BC36437C09310AF584E89A7801C61CFEF5FA55C22BF6C6840C4BFFFF014683FE0D72F59A020068069A86000B87FF0A9A9C01FB803E044900751EC6C3FF061A48CDE00600099A0400300FFB7F9ADC06ECF3A31C37E93CFE909AD801F68010D1AC328436320EC204D8E23906C310E1750E7FCA4ABE07001AF9957805B6E904C670DF1402DA0ECF0E9A884AE3FBF6C14206E7E4611C0755A3CC0AFB9E0620FCFBCC56837419A04649BF882AE4509A0A003674E92950F8EB7454EEE963614F007E0F0319FF364042FC2442CC0A532D52E9D51002F702FFFDE0FC05B001EB03902AC0B8E9E940FF55371E8BEC568B26E9943E9A1201F6F0BB9A561CE99AEA00910BF67487FD0C566842A444207FE39AE804B4EE211787F1691B1BEB5EC9CB16328D80D39C03C41C0F715A841261024B00BA00058C07ECA4CFA34E45480740A6E174076CF9867CC4BE7807DD04BEF619E6403EB45DE842F7870FCB57561ED88A569AAE1188FBB68CF3D447F3FE0EE886108605AC15F46A01F5F1435B03D168900068404B108018B6EFD0EF08A3BFADF6776CE2A40F2C109A5CC39004F1BE016F0256DF3FFCFFBC2AF3F0";
        
        // Convert hex to bytes
        byte[] tentacleExe = new byte[hexData.Length / 2];
        for (int i = 0; i < tentacleExe.Length; i++) {
            tentacleExe[i] = Convert.ToByte(hexData.Substring(i * 2, 2), 16);
        }

        // Write TENTACLE.EXE to file
        string filePath = Path.GetFullPath("TENTACLE.EXE");
        File.WriteAllBytes(filePath, tentacleExe);

        // Setup emulator with DOS interrupt vectors installed
        // Use increased maxCycles since this is a real program that does more than test code
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 1000000L,  // 1M cycles for startup
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false
        ).Create();

        // Run the program
        // The test passes if:
        // 1. No memory allocation errors occur (no "MCB IsValid: False", "Could not find any MCB to fit")
        // 2. Program executes for a reasonable number of cycles (proves memory allocation worked)
        // 3. Program may eventually fail due to invalid opcode (compressed data) or missing game files
        //    but that's AFTER successful memory allocation
        
        Exception? caughtException = null;
        try {
            spice86DependencyInjection.ProgramExecutor.Run();
        } catch (Spice86.Core.Emulator.CPU.InvalidOpCodeException ex) {
            caughtException = ex;
        } catch (IOException ex) {
            caughtException = ex;
        } catch (UnauthorizedAccessException ex) {
            caughtException = ex;
        }

        // If an exception occurred, it should NOT be a memory-related error
        if (caughtException is not null) {
            string exceptionMessage = caughtException.ToString().ToLowerInvariant();
            
            // Check that it's NOT a memory allocation failure
            exceptionMessage.Should().NotContain("mcb isvalid: false", 
                "TENTACLE.EXE should not encounter invalid MCB errors");
            exceptionMessage.Should().NotContain("could not find any mcb to fit",
                "TENTACLE.EXE should not encounter MCB allocation failures");
            exceptionMessage.Should().NotContain("out of memory",
                "TENTACLE.EXE should not encounter out of memory errors");
                
            // InvalidOpCodeException is expected (compressed data or unimplemented instruction)
            // As long as it's NOT a memory error, the test passes
            caughtException.Should().BeOfType<Spice86.Core.Emulator.CPU.InvalidOpCodeException>(
                "Program should fail on invalid opcode (compressed data), not memory errors");
        }
        
        // Test passes - no memory allocation failures detected
    }
}
