namespace Spice86.Tests;

using FluentAssertions;
using Serilog;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.CycleBudget;
using Spice86.Shared.Interfaces;
using System.Runtime.CompilerServices;
using Xunit;

/// <summary>
/// Integration tests for INT 15h AH=86h BIOS Wait function.
/// Includes both direct C# tests and machine code integration tests.
/// </summary>
public class BiosWaitIntegrationTest {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }
    static BiosWaitIntegrationTest() {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    [Fact]
    public void TestBiosWaitSuccess() {
        // Create a minimal machine using Spice86Creator helper
        // Use a simple test binary that exists (we just need a valid machine)
        using var spice86 = new Spice86Creator("add", enableCfgCpu: false, enablePit: true, 
            maxCycles: 100000, installInterruptVectors: true).Create();
        
        var state = spice86.Machine.CpuState;
        var biosDataArea = spice86.Machine.BiosDataArea;
        var systemBiosInt15Handler = spice86.Machine.SystemBiosInt15Handler;

        // Ensure RTC wait flag is initially clear
        biosDataArea.RtcWaitFlag.Should().Be(0, "wait flag should be clear initially");

        // Set up INT 15h AH=86h call - wait for 1000 microseconds (1ms)
        state.AH = 0x86;
        state.CX = 0x0000; // High word of microseconds
        state.DX = 0x03E8; // Low word = 1000 microseconds

        // Call the handler directly
        systemBiosInt15Handler.BiosWait(true);

        // Wait flag should be set during the wait
        biosDataArea.RtcWaitFlag.Should().Be(1, "wait flag should be set during wait");

        // Carry flag should be clear (success)
        state.CarryFlag.Should().BeFalse("carry flag should be clear on success");

        // Note: We don't test event completion here as it requires a full emulator run
        // The event scheduling is tested implicitly by the PIC infrastructure
    }

    [Fact]
    public void TestBiosWaitAlreadyActive() {
        // Create a minimal machine using Spice86Creator helper
        using var spice86 = new Spice86Creator("add", enableCfgCpu: false, enablePit: true,
            maxCycles: 100000, installInterruptVectors: true).Create();
        
        var state = spice86.Machine.CpuState;
        var biosDataArea = spice86.Machine.BiosDataArea;
        var systemBiosInt15Handler = spice86.Machine.SystemBiosInt15Handler;

        // Manually set the wait flag to simulate an active wait
        biosDataArea.RtcWaitFlag = 1;

        // Set up INT 15h AH=86h call
        state.AH = 0x86;
        state.CX = 0x0000;
        state.DX = 0x03E8;

        // Call the handler directly
        systemBiosInt15Handler.BiosWait(true);

        // Should return with error code 0x83 (timer already in use)
        state.CarryFlag.Should().BeTrue("carry flag should be set on error");
        state.AH.Should().Be(0x83, "AH should be 0x83 when timer already in use");
        biosDataArea.RtcWaitFlag.Should().Be(1, "wait flag should still be set");
    }

    [Fact]
    public void TestBiosWaitZeroMicroseconds() {
        // Create a minimal machine using Spice86Creator helper
        using var spice86 = new Spice86Creator("add", enableCfgCpu: false, enablePit: true,
            maxCycles: 100000, installInterruptVectors: true).Create();
        
        var state = spice86.Machine.CpuState;
        var biosDataArea = spice86.Machine.BiosDataArea;
        var systemBiosInt15Handler = spice86.Machine.SystemBiosInt15Handler;

        // Set up INT 15h AH=86h call with 0 microseconds
        state.AH = 0x86;
        state.CX = 0x0000;
        state.DX = 0x0000;

        // Call the handler directly
        systemBiosInt15Handler.BiosWait(true);

        // Should succeed with minimal delay
        state.CarryFlag.Should().BeFalse("carry flag should be clear on success");
        biosDataArea.RtcWaitFlag.Should().Be(1, "wait flag should be set");

        // Note: We don't test event completion here as it requires a full emulator run
        // The event will complete after ~1ms when running in a real emulator
    }

    [Fact]
    public void TestAsmHandlerIsInstalled() {
        // Create a minimal machine with interrupt vectors installed
        using var spice86 = new Spice86Creator("add", enableCfgCpu: false, enablePit: true,
            maxCycles: 100000, installInterruptVectors: true).Create();
        
        var interruptVectorTable = spice86.Machine.Cpu.InterruptVectorTable;

        // Verify that INT 15h vector is installed and points to a valid address
        var int15Vector = interruptVectorTable[0x15];
        int15Vector.Linear.Should().NotBe(0u, "INT 15h vector should point to a valid handler");
        
        // Verify the handler is in the BIOS segment (typically 0xF000)
        int15Vector.Segment.Should().Be(0xF000, "INT 15h handler should be in BIOS segment");

        // Note: This test verifies that the INT 15h handler was installed.
        // The exact ASM generation logic is tested indirectly through the BiosWait() tests.
    }

    /// <summary>
    /// Integration test that executes INT 15h AH=86h through machine code.
    /// Tests the full ASM handler path including the wait loop.
    /// </summary>
    [Fact]
    public void TestBiosWaitThroughInt15h_Success() {
        // Test program that calls INT 15h AH=86h with a short wait (1ms = 1000 microseconds)
        byte[] program = new byte[]
        {
            0xB4, 0x86,             // mov ah, 86h - BIOS Wait function
            0xB9, 0x00, 0x00,       // mov cx, 0 - high word of microseconds
            0xBA, 0xE8, 0x03,       // mov dx, 1000 (0x03E8) - low word = 1000 microseconds (1ms)
            0xCD, 0x15,             // int 15h - call BIOS
            0x73, 0x04,             // jnc success - carry clear means success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        BiosWaitTestHandler testHandler = RunBiosWaitTest(program);
        
        testHandler.Results.Should().Contain((byte)TestResult.Success, 
            "INT 15h AH=86h should complete successfully");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Integration test for zero microseconds wait.
    /// </summary>
    [Fact]
    public void TestBiosWaitThroughInt15h_ZeroMicroseconds() {
        // Test program that calls INT 15h AH=86h with zero microseconds
        byte[] program = new byte[]
        {
            0xB4, 0x86,             // mov ah, 86h - BIOS Wait function
            0xB9, 0x00, 0x00,       // mov cx, 0 - high word of microseconds
            0xBA, 0x00, 0x00,       // mov dx, 0 - low word = 0 microseconds
            0xCD, 0x15,             // int 15h - call BIOS
            0x73, 0x04,             // jnc success - carry clear means success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        BiosWaitTestHandler testHandler = RunBiosWaitTest(program);
        
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "INT 15h AH=86h should handle zero microseconds without error");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Runs a BIOS wait test program and returns a test handler with results.
    /// </summary>
    private BiosWaitTestHandler RunBiosWaitTest(byte[] program,
        [CallerMemberName] string unitTestName = "test") {
        // Write program to a file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator with interrupt vectors and PIT enabled
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: false,  // Use regular CPU for simpler debugging
            enablePit: true,      // Enable PIT for timer support
            recordData: false,
            maxCycles: 1000000L,  // More cycles to allow wait loop to execute
            installInterruptVectors: true,
            enableA20Gate: false
        ).Create();

        BiosWaitTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures BIOS wait test results from designated I/O ports.
    /// </summary>
    private class BiosWaitTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        
        public BiosWaitTestHandler(State state, ILoggerService loggerService,
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
