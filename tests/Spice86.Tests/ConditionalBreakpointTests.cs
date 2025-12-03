namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Tests.Fixtures;

using Xunit;

/// <summary>
/// Tests for conditional breakpoints with AST-based expressions.
/// </summary>
public class ConditionalBreakpointTests : IDisposable {
    private readonly BreakpointTestFixture _fixture;
    private State State => _fixture.State;
    private Memory Memory => _fixture.Memory;
    private EmulatorBreakpointsManager BreakpointsManager => _fixture.BreakpointsManager;
    
    public ConditionalBreakpointTests() {
        _fixture = new BreakpointTestFixture();
    }
    
    public void Dispose() {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
    
    [Fact]
    public void TestConditionalMemoryBreakpoint() {
        // Set up test data
        Memory.UInt8[0x100] = 0x42;
        State.AX = 0x100;
        
        // Create a conditional breakpoint that only triggers when ax == 0x100
        int triggerCount = 0;
        AddressBreakPoint breakpoint = _fixture.CreateConditionalBreakpoint(
            BreakPointType.MEMORY_READ,
            0x100,
            _ => triggerCount++,
            "ax == 0x100");
        
        BreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger the breakpoint (ax is 0x100)
        _ = Memory.UInt8[0x100];
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        State.AX = 0x200;
        
        // This should not trigger the breakpoint
        _ = Memory.UInt8[0x100];
        triggerCount.Should().Be(1);
        
        BreakpointsManager.ToggleBreakPoint(breakpoint, false);
    }
    
    [Fact]
    public void TestConditionalBreakpointSerialization() {
        // Set up test data
        Memory.UInt8[0x200] = 0x55;
        State.AX = 0x10;
        
        // Create a conditional breakpoint with an expression
        string conditionExpression = "ax == 0x10";
        Func<long, bool> condition = _fixture.CompileCondition(conditionExpression);
        
        AddressBreakPoint breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_READ,
            0x200,
            _ => { },
            false,
            condition,
            conditionExpression) {
            IsUserBreakpoint = true,
            IsEnabled = true
        };
        
        BreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // Serialize
        Spice86.Shared.Emulator.VM.Breakpoint.Serializable.SerializableUserBreakpointCollection serialized = BreakpointsManager.CreateSerializableBreakpoints();
        
        // Verify the condition expression was serialized
        serialized.Breakpoints.Should().HaveCount(1);
        serialized.Breakpoints[0].ConditionExpression.Should().Be(conditionExpression);
        serialized.Breakpoints[0].Trigger.Should().Be(0x200);
        serialized.Breakpoints[0].Type.Should().Be(BreakPointType.MEMORY_READ);
        serialized.Breakpoints[0].IsEnabled.Should().BeTrue();
        
        // Remove the breakpoint
        BreakpointsManager.ToggleBreakPoint(breakpoint, false);
        
        // Restore from serialized data
        BreakpointsManager.RestoreBreakpoints(serialized);
        
        // Test that the restored breakpoint works correctly
        // We need to replace the breakpoint's onReached action since we can't serialize that
        // In practice, this is handled by the BreakpointsViewModel
        IEnumerable<AddressBreakPoint> restoredBreakpoints = BreakpointsManager.MemoryReadWriteBreakpoints.SerializableBreakpoints;
        restoredBreakpoints.Should().HaveCount(1);
        restoredBreakpoints.First().ConditionExpression.Should().Be(conditionExpression);
    }
    
    [Fact]
    public void TestComplexConditionalExpression() {
        // Set up test data
        Memory.UInt8[0x300] = 0x10;
        Memory.UInt8[0x301] = 0x20;
        State.AX = 0x30;
        State.BX = 0x20;
        
        // Create a complex conditional: ax == 0x30 && bx > 0x10
        int triggerCount = 0;
        AddressBreakPoint breakpoint = _fixture.CreateConditionalBreakpoint(
            BreakPointType.MEMORY_WRITE,
            0x300,
            _ => triggerCount++,
            "ax == 0x30 && bx > 0x10");
        
        BreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger (ax == 0x30 and bx > 0x10)
        Memory.UInt8[0x300] = 0x15;
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        State.AX = 0x40;
        
        // This should not trigger
        Memory.UInt8[0x300] = 0x16;
        triggerCount.Should().Be(1);
        
        BreakpointsManager.ToggleBreakPoint(breakpoint, false);
    }
}
