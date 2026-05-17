namespace Spice86.Tests;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using FluentAssertions;
using Xunit;

public class ColdPathBreakpointTests {
    [Fact]
    public void ColdPath_ExecutionBreakpoint_FiresAtCorrectAddress() {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add").Create();
        State state = spice86DependencyInjection.Machine.CpuState;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        EmulatorBreakpointsManager emulatorBreakpointsManager =
            spice86DependencyInjection.Machine.EmulatorBreakpointsManager;

        // Address of "add bx, ax" instruction in add.asm (F000:0009)
        uint targetAddress = 0xF0009;
        bool breakpointFired = false;
        uint addressAtBreakpoint = 0;

        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(
            BreakPointType.CPU_EXECUTION_ADDRESS, targetAddress, _ => {
                breakpointFired = true;
                addressAtBreakpoint = state.IpPhysicalAddress;
            }, true), true);

        programExecutor.Run();

        breakpointFired.Should().BeTrue();
        addressAtBreakpoint.Should().Be(targetAddress);
    }
}
