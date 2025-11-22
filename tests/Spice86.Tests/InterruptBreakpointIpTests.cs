namespace Spice86.Tests;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

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
        var pauseHandler = spice86DependencyInjection.Machine.PauseHandler;

        SegmentedAddress? capturedInCallback = null;
        SegmentedAddress? capturedInPausedEvent = null;

        // This flag tracks when the Paused event fires
        var pausedEventFired = new System.Threading.ManualResetEvent(false);
        
        // Subscribe to Paused event like the UI does
        pauseHandler.Paused += () => {
            // This simulates what the UI does in OnPaused - immediately read State.IP
            capturedInPausedEvent = state.IpSegmentedAddress;
            pausedEventFired.Set();
        };

        // Set up a breakpoint on INT 0Dh (which is in the interrupt test binary)
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(
            BreakPointType.CPU_INTERRUPT, 
            0xD, 
            breakpoint => {
                // Capture the current IP when the breakpoint callback is invoked (synchronous)
                capturedInCallback = state.IpSegmentedAddress;
                // Call RequestPause like normal breakpoints do
                pauseHandler.RequestPause($"Breakpoint {breakpoint.BreakPointType} reached");
                // Immediately resume so the test doesn't hang
                pauseHandler.Resume();
            }, 
            false), 
            true);

        // Run the program which will trigger INT 0Dh
        programExecutor.Run();

        // Verify that both captures point to the INT instruction (opcode CD), not past it
        Assert.NotNull(capturedInCallback);
        Assert.NotNull(capturedInPausedEvent);
        
        // The byte at the captured IP from the callback should be 0xCD (INT opcode)
        byte opcodeAtCallbackIp = memory.UInt8[MemoryUtils.ToPhysicalAddress(capturedInCallback.Value.Segment, capturedInCallback.Value.Offset)];
        Assert.Equal(0xCD, opcodeAtCallbackIp);
        
        // The byte at the captured IP from the Paused event should ALSO be 0xCD (INT opcode)
        byte opcodeAtPausedEventIp = memory.UInt8[MemoryUtils.ToPhysicalAddress(capturedInPausedEvent.Value.Segment, capturedInPausedEvent.Value.Offset)];
        Assert.Equal(0xCD, opcodeAtPausedEventIp);
    }

    private static Spice86DependencyInjection CreateEmulatorWithIntInstruction(bool enableCfgCpu) {
        // Use the "interrupt" test binary which contains interrupt instructions
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("interrupt", 
            enableCfgCpu: enableCfgCpu, 
            installInterruptVectors: true).Create();
        
        return spice86DependencyInjection;
    }
}
