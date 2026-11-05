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
            (_, _) => { },
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
            (_, _) => triggered = true,
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
            (_, _) => { },
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
            (_, _) => triggerCount++,
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
            (_, _) => { },
            false);
        AddressBreakPoint enabledBreakpoint2 = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x200,
            (_, _) => { },
            false);
        
        holder.ToggleBreakPoint(enabledBreakpoint1, true);
        holder.ToggleBreakPoint(enabledBreakpoint2, true);
        
        // Both enabled - should return true
        Assert.True(holder.HasActiveBreakpoints);
        
        // Create disabled breakpoints
        AddressBreakPoint disabledBreakpoint1 = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x300,
            (_, _) => { },
            false) {
            IsEnabled = false
        };
        AddressBreakPoint disabledBreakpoint2 = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0x400,
            (_, _) => { },
            false) {
            IsEnabled = false
        };
        
        holder.ToggleBreakPoint(disabledBreakpoint1, true);
        holder.ToggleBreakPoint(disabledBreakpoint2, true);
        
        // Mix of enabled and disabled - should return true (counter reflects at least one enabled)
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

    [Fact]
    public void RemovingOneBreakpointKeepsNeighbouringAddressActive() {
        // Arrange
        int triggered1000 = 0;
        int triggered1001 = 0;
        BreakPointHolder holder = new();
        AddressBreakPoint breakPoint1000 = new(
            BreakPointType.MEMORY_WRITE,
            0x1000,
            (_, _) => triggered1000++,
            false);
        AddressBreakPoint breakPoint1001 = new(
            BreakPointType.MEMORY_WRITE,
            0x1001,
            (_, _) => triggered1001++,
            false);
        holder.ToggleBreakPoint(breakPoint1000, true);
        holder.ToggleBreakPoint(breakPoint1001, true);

        // Act
        holder.ToggleBreakPoint(breakPoint1000, false);
        holder.TriggerMatchingBreakPoints(0x1000);
        holder.TriggerMatchingBreakPoints(0x1001);

        // Assert
        Assert.Equal(0, triggered1000);
        Assert.Equal(1, triggered1001);
    }

    [Fact]
    public void RemovingOneBreakpointKeepsOtherBreakpointOnSameAddressActive() {
        // Arrange
        int triggered1 = 0;
        int triggered2 = 0;
        BreakPointHolder holder = new();
        AddressBreakPoint breakPoint1 = new(
            BreakPointType.MEMORY_WRITE,
            0x2000,
            (_, _) => triggered1++,
            false);
        AddressBreakPoint breakPoint2 = new(
            BreakPointType.MEMORY_WRITE,
            0x2000,
            (_, _) => triggered2++,
            false);
        holder.ToggleBreakPoint(breakPoint1, true);
        holder.ToggleBreakPoint(breakPoint2, true);

        // Act
        holder.ToggleBreakPoint(breakPoint1, false);
        holder.TriggerMatchingBreakPoints(0x2000);

        // Assert
        Assert.Equal(0, triggered1);
        Assert.Equal(1, triggered2);
    }

    [Fact]
    public void RemoveOnTriggerBreakpointFiresOnceAndAddressCanBeReused() {
        // Arrange
        int firstTriggerCount = 0;
        int secondTriggerCount = 0;
        BreakPointHolder holder = new();
        AddressBreakPoint removeOnTrigger = new(
            BreakPointType.MEMORY_WRITE,
            0x3000,
            (_, _) => firstTriggerCount++,
            true);
        holder.ToggleBreakPoint(removeOnTrigger, true);

        // Act
        holder.TriggerMatchingBreakPoints(0x3000);
        holder.TriggerMatchingBreakPoints(0x3000);
        AddressBreakPoint newBreakPoint = new(
            BreakPointType.MEMORY_WRITE,
            0x3000,
            (_, _) => secondTriggerCount++,
            false);
        holder.ToggleBreakPoint(newBreakPoint, true);
        holder.TriggerMatchingBreakPoints(0x3000);

        // Assert
        Assert.Equal(1, firstTriggerCount);
        Assert.Equal(1, secondTriggerCount);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData((1L << 21) - 1)]
    [InlineData(1L << 21)]
    [InlineData(10_000_000_000L)]
    public void AddressBreakPointTriggersAtFilterEdgesAndBeyond(long address) {
        // Arrange
        int triggerCount = 0;
        BreakPointHolder holder = new();
        AddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            address,
            (_, _) => triggerCount++,
            false);
        holder.ToggleBreakPoint(breakPoint, true);

        // Act
        holder.TriggerMatchingBreakPoints(address);

        // Assert
        Assert.Equal(1, triggerCount);
    }

    [Fact]
    public void MultiAddressBreakPointMatchesOnlyItsRegisteredAddresses() {
        // Arrange
        BreakPointHolder holder = new();
        List<long> reached = [];
        MultiAddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            (_, address) => reached.Add(address));
        for (long address = 0x100; address < 0x110; address++) {
            holder.ToggleBreakPointAt(breakPoint, address, true);
        }

        // Act
        Assert.False(holder.TriggerMatchingBreakPoints(0x0FF));
        Assert.True(holder.TriggerMatchingBreakPoints(0x100));
        Assert.True(holder.TriggerMatchingBreakPoints(0x10F));
        Assert.False(holder.TriggerMatchingBreakPoints(0x110));

        // Assert
        Assert.Equal(new long[] { 0x100, 0x10F }, reached);
    }

    [Fact]
    public void MultiAddressBreakPointRemoveStopsOnlyThatAddress() {
        // Arrange
        BreakPointHolder holder = new();
        List<long> reached = [];
        MultiAddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            (_, address) => reached.Add(address));
        for (long address = 0x100; address < 0x110; address++) {
            holder.ToggleBreakPointAt(breakPoint, address, true);
        }

        // Act
        holder.ToggleBreakPointAt(breakPoint, 0x105, false);
        holder.TriggerMatchingBreakPoints(0x105);
        holder.TriggerMatchingBreakPoints(0x104);
        holder.TriggerMatchingBreakPoints(0x106);

        // Assert
        Assert.Equal(new long[] { 0x104, 0x106 }, reached);
    }

    [Fact]
    public void MultiAddressBreakPointIsRegisteredOnceForEveryAddressAndRemovedAfterTheLast() {
        // Arrange
        BreakPointHolder holder = new();
        MultiAddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            (_, _) => { });
        holder.ToggleBreakPointAt(breakPoint, 0x100, true);
        Assert.True(holder.HasActiveBreakpoints);

        // Act + Assert: a second address does not register the breakpoint again; removing the
        // first leaves the object registered for the second.
        holder.ToggleBreakPointAt(breakPoint, 0x101, true);
        Assert.True(holder.TriggerMatchingBreakPoints(0x101));
        holder.ToggleBreakPointAt(breakPoint, 0x100, false);
        Assert.True(holder.HasActiveBreakpoints);
        Assert.True(holder.TriggerMatchingBreakPoints(0x101));

        holder.ToggleBreakPointAt(breakPoint, 0x101, false);
        Assert.False(holder.HasActiveBreakpoints);
        Assert.True(holder.IsEmpty);
    }

    [Fact]
    public void DisabledMultiAddressBreakPointFiresNothing() {
        // Arrange
        BreakPointHolder holder = new();
        int triggers = 0;
        MultiAddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            (_, _) => triggers++);
        holder.ToggleBreakPointAt(breakPoint, 0x105, true);

        // Act
        breakPoint.IsEnabled = false;
        holder.TriggerMatchingBreakPoints(0x105);

        // Assert
        Assert.Equal(0, triggers);
    }

    [Fact]
    public void MultiAddressBreakPointWorksNextToAnAddressBreakPointOnTheSameByte() {
        // Arrange
        BreakPointHolder holder = new();
        int multiTriggers = 0;
        int addressTriggers = 0;
        MultiAddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            (_, _) => multiTriggers++);
        holder.ToggleBreakPointAt(breakPoint, 0x205, true);
        holder.ToggleBreakPoint(new AddressBreakPoint(
            BreakPointType.MEMORY_WRITE, 0x205, (_, _) => addressTriggers++, false), true);

        // Act
        Assert.True(holder.TriggerMatchingBreakPoints(0x205));

        // Assert
        Assert.Equal(1, multiTriggers);
        Assert.Equal(1, addressTriggers);
    }

    [Fact]
    public void MultiAddressBreakPointIsNotInTheSerializableLists() {
        // Arrange
        BreakPointHolder holder = new();
        MultiAddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            (_, _) => { });
        holder.ToggleBreakPointAt(breakPoint, 0x100, true);

        // Act + Assert
        Assert.Empty(holder.SerializableBreakpoints);
        Assert.Empty(holder.SerializableWildcardBreakpoints);
    }

    [Fact]
    public void MultiAddressBreakPointToggledOffAtTheTriggeringAddressByItsOwnActionDoesNotThrow() {
        // Arrange
        BreakPointHolder holder = new();
        int otherTriggers = 0;
        MultiAddressBreakPoint breakPoint = new(
            BreakPointType.MEMORY_WRITE,
            (bp, address) => holder.ToggleBreakPointAt(bp, address, false));
        holder.ToggleBreakPointAt(breakPoint, 0x105, true);
        holder.ToggleBreakPointAt(breakPoint, 0x106, true);
        holder.ToggleBreakPoint(new AddressBreakPoint(
            BreakPointType.MEMORY_WRITE, 0x105, (_, _) => otherTriggers++, false), true);

        // Act
        Assert.True(holder.TriggerMatchingBreakPoints(0x105));
        bool triggeredAt105Again = holder.TriggerMatchingBreakPoints(0x105);
        bool triggeredAt106 = holder.TriggerMatchingBreakPoints(0x106);

        // Assert: removing itself at 0x105 kept the other breakpoint at that address firing on
        // both calls and left the breakpoint armed at 0x106.
        Assert.Equal(2, otherTriggers);
        Assert.True(triggeredAt105Again);
        Assert.True(triggeredAt106);
    }
}
