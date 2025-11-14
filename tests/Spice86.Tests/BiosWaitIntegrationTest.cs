namespace Spice86.Tests;

using FluentAssertions;
using Serilog;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.CycleBudget;
using Xunit;

/// <summary>
/// Integration tests for INT 15h AH=86h BIOS Wait function
/// </summary>
public class BiosWaitIntegrationTest {
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
}
