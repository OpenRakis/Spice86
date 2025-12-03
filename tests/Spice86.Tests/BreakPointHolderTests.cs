namespace Spice86.Tests;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Xunit;

public class BreakPointHolderTests {
    [Fact]
    public void HasActiveBreakpointsTracksEnabledState() {
        BreakPointHolder holder = new();
        AddressBreakPoint breakPoint = new(
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
        AddressBreakPoint breakPoint = new(
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
        UnconditionalBreakPoint breakPoint = new(
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
        AddressBreakPoint breakPoint = new(
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
    public void HasActiveBreakpointsWithMultipleBreakpointsInMixedStates() {
        BreakPointHolder holder = new();
        
        // Create multiple enabled breakpoints
        AddressBreakPoint enabledBreakpoint1 = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x100,
            _ => { },
            false);
        AddressBreakPoint enabledBreakpoint2 = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x200,
            _ => { },
            false);
        
        holder.ToggleBreakPoint(enabledBreakpoint1, true);
        holder.ToggleBreakPoint(enabledBreakpoint2, true);
        
        // Both enabled - should return true
        Assert.True(holder.HasActiveBreakpoints);
        
        // Create disabled breakpoints
        AddressBreakPoint disabledBreakpoint1 = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x300,
            _ => { },
            false) {
            IsEnabled = false
        };
        AddressBreakPoint disabledBreakpoint2 = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x400,
            _ => { },
            false) {
            IsEnabled = false
        };
        
        holder.ToggleBreakPoint(disabledBreakpoint1, true);
        holder.ToggleBreakPoint(disabledBreakpoint2, true);
        
        // Mix of enabled and disabled - should return true (early return on first enabled)
        Assert.True(holder.HasActiveBreakpoints);
        
        // Disable all enabled breakpoints
        enabledBreakpoint1.IsEnabled = false;
        enabledBreakpoint2.IsEnabled = false;
        
        // All disabled - should return false
        Assert.False(holder.HasActiveBreakpoints);
        
        // Re-enable one
        enabledBreakpoint1.IsEnabled = true;
        
        // At least one enabled - should return true
        Assert.True(holder.HasActiveBreakpoints);
    }
}
