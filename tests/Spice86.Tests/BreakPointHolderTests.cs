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
}