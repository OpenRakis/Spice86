namespace Spice86.Tests;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// Tests to verify that when an interrupt breakpoint is hit, the IP points to the INT instruction,
/// not to the instruction after it.
/// </summary>
public class InterruptBreakpointIpTests {
    public static IEnumerable<object[]> GetCfgCpuConfigurations() {
        yield return new object[] { false };
        yield return new object[] { true };
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestInterruptBreakpointShowsCorrectIp(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = CreateEmulatorWithIntInstruction(enableCfgCpu);
        State state = spice86DependencyInjection.Machine.CpuState;
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IMemory memory = spice86DependencyInjection.Machine.Memory;

        SegmentedAddress? capturedAddress = null;
        uint? capturedPhysicalIp = null;

        // Set up a breakpoint on INT 0Dh (which is in the interrupt test binary)
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(
            BreakPointType.CPU_INTERRUPT, 
            0xD, 
            breakpoint => {
                // Capture the current IP when the breakpoint is hit
                capturedAddress = state.IpSegmentedAddress;
                capturedPhysicalIp = state.IpPhysicalAddress;
            }, 
            false), 
            true);

        // Run the program which will trigger INT 0Dh
        programExecutor.Run();

        // Verify that the captured IP points to the INT instruction (opcode CD), not past it
        Assert.NotNull(capturedAddress);
        Assert.NotNull(capturedPhysicalIp);
        
        // The byte at the captured IP should be 0xCD (INT opcode)
        byte opcodeAtCapturedIp = memory.UInt8[capturedPhysicalIp.Value];
        Assert.Equal(0xCD, opcodeAtCapturedIp);
    }

    private static Spice86DependencyInjection CreateEmulatorWithIntInstruction(bool enableCfgCpu) {
        // Use the "interrupt" test binary which contains interrupt instructions
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("interrupt", 
            enableCfgCpu: enableCfgCpu, 
            installInterruptVectors: true).Create();
        
        return spice86DependencyInjection;
    }
}
