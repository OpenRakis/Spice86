namespace Spice86.Tests;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

public class BreakPointHolderTests {
    [Fact]
    public void HasActiveBreakpointsTracksEnabledState() {
        BreakPointHolder holder = new();
        var breakPoint = new AddressBreakPoint(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x10,
            _ => { },
            false);

        Assert.False(holder.HasActiveBreakpoints);

        holder.ToggleBreakPoint(breakPoint, true);
        Assert.True(holder.HasActiveBreakpoints);

        breakPoint.IsEnabled = false;
        Assert.False(holder.HasActiveBreakpoints);

        breakPoint.IsEnabled = true;
        Assert.True(holder.HasActiveBreakpoints);

        holder.ToggleBreakPoint(breakPoint, false);
        Assert.False(holder.HasActiveBreakpoints);
    }

    [Fact]
    public void RemovalOnTriggerUpdatesActiveBreakpoints() {
        bool triggered = false;
        BreakPointHolder holder = new();
        var breakPoint = new AddressBreakPoint(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x20,
            _ => triggered = true,
            true);

        holder.ToggleBreakPoint(breakPoint, true);
        Assert.True(holder.HasActiveBreakpoints);

        holder.TriggerMatchingBreakPoints(0x20);

        Assert.True(triggered);
        Assert.False(holder.HasActiveBreakpoints);
    }

    [Fact]
    public void DoubleToggleUnconditionalBreakPointDoesNotLeakActiveCount() {
        BreakPointHolder holder = new();
        var breakPoint = new UnconditionalBreakPoint(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            _ => { },
            false);

        holder.ToggleBreakPoint(breakPoint, true);
        holder.ToggleBreakPoint(breakPoint, true);
        Assert.True(holder.HasActiveBreakpoints);

        holder.ToggleBreakPoint(breakPoint, false);
        Assert.False(holder.HasActiveBreakpoints);
    }

    [Fact]
    public void AddressBreakPointIsOnlyRegisteredOnce() {
        int triggerCount = 0;
        BreakPointHolder holder = new();
        var breakPoint = new AddressBreakPoint(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x30,
            _ => triggerCount++,
            false);

        holder.ToggleBreakPoint(breakPoint, true);
        holder.ToggleBreakPoint(breakPoint, true);

        holder.TriggerMatchingBreakPoints(0x30);
        Assert.Equal(1, triggerCount);

        holder.ToggleBreakPoint(breakPoint, false);
        Assert.False(holder.HasActiveBreakpoints);
    }

    [Fact]
    public void SerializableBreakpointsAreDeterministicallyOrdered() {
        BreakPointHolder holder = new();
        
        // Add breakpoints in reverse order to test sorting
        var breakPoint3 = new AddressBreakPoint(BreakPointType.CPU_INTERRUPT, 0x30, _ => { }, false) { IsUserBreakpoint = true };
        var breakPoint1 = new AddressBreakPoint(BreakPointType.CPU_INTERRUPT, 0x10, _ => { }, false) { IsUserBreakpoint = true };
        var breakPoint2 = new AddressBreakPoint(BreakPointType.CPU_INTERRUPT, 0x20, _ => { }, false) { IsUserBreakpoint = true };
        
        holder.ToggleBreakPoint(breakPoint3, true);
        holder.ToggleBreakPoint(breakPoint1, true);
        holder.ToggleBreakPoint(breakPoint2, true);

        // Get serializable breakpoints multiple times and verify order is consistent
        var firstIteration = holder.SerializableBreakpoints.ToList();
        var secondIteration = holder.SerializableBreakpoints.ToList();
        
        Assert.Equal(3, firstIteration.Count);
        Assert.Equal(3, secondIteration.Count);
        
        // Verify breakpoints are ordered by address
        Assert.Equal(0x10, firstIteration[0].Address);
        Assert.Equal(0x20, firstIteration[1].Address);
        Assert.Equal(0x30, firstIteration[2].Address);
        
        // Verify order is consistent across iterations
        for (int i = 0; i < firstIteration.Count; i++) {
            Assert.Equal(firstIteration[i].Address, secondIteration[i].Address);
        }
    }
}
