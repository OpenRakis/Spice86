namespace Spice86.Tests;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

using Xunit;

/// <summary>
/// Tests to verify that when an interrupt breakpoint is hit, the pause handler is called
/// and the IP can be captured for the debugger UI.
/// </summary>
public class InterruptBreakpointIpTests {
    [Fact]
    public void TestInterruptBreakpointCallsPauseHandler() {
        // Test with CfgCpu only (regular CPU will be removed)
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("interrupt",
            enableCfgCpu: true,
            installInterruptVectors: true).Create();
        
        State state = spice86DependencyInjection.Machine.CpuState;
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IMemory memory = spice86DependencyInjection.Machine.Memory;
        IPauseHandler pauseHandler = spice86DependencyInjection.Machine.PauseHandler;

        SegmentedAddress? capturedInCallback = null;
        SegmentedAddress? capturedInPausedEvent = null;

        // This flag tracks when the Paused event fires
        using System.Threading.ManualResetEvent pausedEventFired = new(false);
        
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

        // Verify that the breakpoint was hit and pause handler was called
        Assert.NotNull(capturedInCallback);
        Assert.NotNull(capturedInPausedEvent);
        
        // With the Core fix, State.IP should point to the INT instruction when breakpoint fires
        // Verify that the byte at the captured IP is 0xCD (INT opcode)
        byte opcodeAtCallbackIp = memory.UInt8[MemoryUtils.ToPhysicalAddress(capturedInCallback.Value.Segment, capturedInCallback.Value.Offset)];
        Assert.Equal(0xCD, opcodeAtCallbackIp);
        
        // The Paused event should also see the correct IP
        byte opcodeAtPausedEventIp = memory.UInt8[MemoryUtils.ToPhysicalAddress(capturedInPausedEvent.Value.Segment, capturedInPausedEvent.Value.Offset)];
        Assert.Equal(0xCD, opcodeAtPausedEventIp);
    }
}
