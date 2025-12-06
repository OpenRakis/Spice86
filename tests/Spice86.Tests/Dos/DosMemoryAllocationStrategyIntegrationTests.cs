namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for DOS memory allocation strategy (INT 21h/58h) that run machine code
/// through the emulation stack, following the same pattern as XMS and EMS integration tests.
/// </summary>
public class DosMemoryAllocationStrategyIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results

    private enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests INT 21h/58h subfunction 0x00 - Get allocation strategy.
    /// Default strategy should be FirstFit (0x00).
    /// </summary>
    [Fact]
    public void GetAllocationStrategy_ShouldReturnFirstFitByDefault() {
        // INT 21h/58h: AH=58h, AL=00h (Get allocation strategy)
        // On return: AX = current strategy
        byte[] program = new byte[] {
            0xB8, 0x00, 0x58,       // mov ax, 5800h - Get allocation strategy
            0xCD, 0x21,             // int 21h
            0x72, 0x06,             // jc failed - Jump if carry (error)
            0x3D, 0x00, 0x00,       // cmp ax, 0000h - FirstFit expected
            0x74, 0x04,             // je success
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

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h/58h subfunction 0x01 - Set allocation strategy to BestFit.
    /// </summary>
    [Fact]
    public void SetAllocationStrategy_ToBestFit_ShouldSucceed() {
        // INT 21h/58h: AH=58h, AL=01h (Set allocation strategy)
        // BX = new strategy (01h = BestFit)
        byte[] program = new byte[] {
            0xB8, 0x01, 0x58,       // mov ax, 5801h - Set allocation strategy
            0xBB, 0x01, 0x00,       // mov bx, 0001h - BestFit
            0xCD, 0x21,             // int 21h
            0x72, 0x0B,             // jc failed - Jump if carry (error)
            // Verify by getting strategy back
            0xB8, 0x00, 0x58,       // mov ax, 5800h - Get allocation strategy
            0xCD, 0x21,             // int 21h
            0x3D, 0x01, 0x00,       // cmp ax, 0001h - BestFit expected
            0x74, 0x04,             // je success
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

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h/58h subfunction 0x01 - Set allocation strategy to LastFit.
    /// </summary>
    [Fact]
    public void SetAllocationStrategy_ToLastFit_ShouldSucceed() {
        // INT 21h/58h: AH=58h, AL=01h (Set allocation strategy)
        // BX = new strategy (02h = LastFit)
        byte[] program = new byte[] {
            0xB8, 0x01, 0x58,       // mov ax, 5801h - Set allocation strategy
            0xBB, 0x02, 0x00,       // mov bx, 0002h - LastFit
            0xCD, 0x21,             // int 21h
            0x72, 0x0B,             // jc failed - Jump if carry (error)
            // Verify by getting strategy back
            0xB8, 0x00, 0x58,       // mov ax, 5800h - Get allocation strategy
            0xCD, 0x21,             // int 21h
            0x3D, 0x02, 0x00,       // cmp ax, 0002h - LastFit expected
            0x74, 0x04,             // je success
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

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h/58h subfunction 0x01 - Invalid fit type (> 0x02) should fail.
    /// </summary>
    [Fact]
    public void SetAllocationStrategy_InvalidFitType_ShouldFail() {
        // INT 21h/58h: AH=58h, AL=01h (Set allocation strategy)
        // BX = new strategy (03h = Invalid)
        byte[] program = new byte[] {
            0xB8, 0x01, 0x58,       // mov ax, 5801h - Set allocation strategy
            0xBB, 0x03, 0x00,       // mov bx, 0003h - Invalid fit type
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed - Should have carry set
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
    /// Tests INT 21h/58h subfunction 0x01 - Bits 2-5 set should fail.
    /// </summary>
    [Fact]
    public void SetAllocationStrategy_Bits2To5Set_ShouldFail() {
        // INT 21h/58h: AH=58h, AL=01h (Set allocation strategy)
        // BX = new strategy (04h = Bit 2 set - Invalid)
        byte[] program = new byte[] {
            0xB8, 0x01, 0x58,       // mov ax, 5801h - Set allocation strategy
            0xBB, 0x04, 0x00,       // mov bx, 0004h - Bit 2 set (invalid)
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed - Should have carry set
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
    /// Tests INT 21h/58h subfunction 0x01 - Invalid high memory bits (0xC0) should fail.
    /// </summary>
    [Fact]
    public void SetAllocationStrategy_InvalidHighMemBits_ShouldFail() {
        // INT 21h/58h: AH=58h, AL=01h (Set allocation strategy)
        // BX = new strategy (C0h = Both high bits set - Invalid)
        byte[] program = new byte[] {
            0xB8, 0x01, 0x58,       // mov ax, 5801h - Set allocation strategy
            0xBB, 0xC0, 0x00,       // mov bx, 00C0h - Both bits 6 and 7 set (invalid)
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed - Should have carry set
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
    /// Tests INT 21h/58h subfunction 0x02 - Get UMB link state should fail (not implemented).
    /// </summary>
    [Fact]
    public void GetUmbLinkState_ShouldFail() {
        // INT 21h/58h: AH=58h, AL=02h (Get UMB link state)
        byte[] program = new byte[] {
            0xB8, 0x02, 0x58,       // mov ax, 5802h - Get UMB link state
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed - Should have carry set (not implemented)
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
    /// Tests INT 21h/58h subfunction 0x03 - Set UMB link state should fail (not implemented).
    /// </summary>
    [Fact]
    public void SetUmbLinkState_ShouldFail() {
        // INT 21h/58h: AH=58h, AL=03h (Set UMB link state)
        // BX = 0 (unlink UMBs)
        byte[] program = new byte[] {
            0xB8, 0x03, 0x58,       // mov ax, 5803h - Set UMB link state
            0xBB, 0x00, 0x00,       // mov bx, 0000h - Unlink UMBs
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed - Should have carry set (not implemented)
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
    /// Tests INT 21h/58h invalid subfunction should fail.
    /// </summary>
    [Fact]
    public void InvalidSubfunction_ShouldFail() {
        // INT 21h/58h: AH=58h, AL=FFh (Invalid subfunction)
        byte[] program = new byte[] {
            0xB8, 0xFF, 0x58,       // mov ax, 58FFh - Invalid subfunction
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed - Should have carry set
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
    /// Tests INT 21h/58h set and then get allocation strategy cycle.
    /// </summary>
    [Fact]
    public void SetThenGetAllocationStrategy_ShouldRoundTrip() {
        // Set to LastFit, get it back, verify
        byte[] program = new byte[] {
            // Set to LastFit (0x02)
            0xB8, 0x01, 0x58,       // mov ax, 5801h - Set allocation strategy
            0xBB, 0x02, 0x00,       // mov bx, 0002h - LastFit
            0xCD, 0x21,             // int 21h
            0x72, 0x16,             // jc failed - Jump if carry (error)
            // Get it back
            0xB8, 0x00, 0x58,       // mov ax, 5800h - Get allocation strategy
            0xCD, 0x21,             // int 21h
            0x72, 0x10,             // jc failed
            // Verify it's LastFit
            0x3D, 0x02, 0x00,       // cmp ax, 0002h
            0x75, 0x0B,             // jne failed
            // Set back to FirstFit
            0xB8, 0x01, 0x58,       // mov ax, 5801h
            0xBB, 0x00, 0x00,       // mov bx, 0000h - FirstFit
            0xCD, 0x21,             // int 21h
            0x72, 0x02,             // jc failed
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

        public DosTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            }
        }
    }
}
