namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

using Xunit;

/// <summary>
/// Tests for conditional breakpoints with AST-based expressions.
/// </summary>
public class ConditionalBreakpointTests {
    public static IEnumerable<object[]> GetCfgCpuConfigurations() {
        yield return new object[] { false };
        yield return new object[] { true };
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestConditionalMemoryBreakpoint(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        var memory = spice86DependencyInjection.Machine.Memory;
        var state = spice86DependencyInjection.Machine.CpuState;
        
        // Set up test data
        memory.UInt8[0x100] = 0x42;
        state.AX = 0x100;
        
        // Create a conditional breakpoint that only triggers when byte[address] == 0x42
        int triggerCount = 0;
        var breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_READ,
            0x100,
            _ => triggerCount++,
            false,
            address => {
                var context = new BreakpointExpressionContext(state, memory, address);
                var parser = new Shared.Emulator.VM.Breakpoint.Expression.ExpressionParser();
                var ast = parser.Parse("byte[address] == 0x42");
                return ast.Evaluate(context) != 0;
            },
            "byte[address] == 0x42");
        
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger the breakpoint
        _ = memory.UInt8[0x100];
        triggerCount.Should().Be(1);
        
        // Change the value
        memory.UInt8[0x100] = 0x43;
        
        // This should not trigger the breakpoint
        _ = memory.UInt8[0x100];
        triggerCount.Should().Be(1);
        
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, false);
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestConditionalBreakpointSerialization(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        var memory = spice86DependencyInjection.Machine.Memory;
        var state = spice86DependencyInjection.Machine.CpuState;
        var pauseHandler = spice86DependencyInjection.Machine.PauseHandler;
        
        // Set up test data
        memory.UInt8[0x200] = 0x55;
        state.AX = 0x10;
        
        // Create a conditional breakpoint with an expression
        string conditionExpression = "ax == 0x10";
        var parser = new Shared.Emulator.VM.Breakpoint.Expression.ExpressionParser();
        var ast = parser.Parse(conditionExpression);
        
        var breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_READ,
            0x200,
            _ => { },
            false,
            address => {
                var context = new BreakpointExpressionContext(state, memory, address);
                return ast.Evaluate(context) != 0;
            },
            conditionExpression) {
            IsUserBreakpoint = true,
            IsEnabled = true
        };
        
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // Serialize
        var serialized = emulatorBreakpointsManager.CreateSerializableBreakpoints();
        
        // Verify the condition expression was serialized
        serialized.Breakpoints.Should().HaveCount(1);
        serialized.Breakpoints[0].ConditionExpression.Should().Be(conditionExpression);
        serialized.Breakpoints[0].Trigger.Should().Be(0x200);
        serialized.Breakpoints[0].Type.Should().Be(BreakPointType.MEMORY_READ);
        serialized.Breakpoints[0].IsEnabled.Should().BeTrue();
        
        // Remove the breakpoint
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, false);
        
        // Restore from serialized data
        emulatorBreakpointsManager.RestoreBreakpoints(serialized);
        
        // Test that the restored breakpoint works correctly
        int triggerCount = 0;
        // We need to replace the breakpoint's onReached action since we can't serialize that
        // In practice, this is handled by the BreakpointsViewModel
        var restoredBreakpoints = emulatorBreakpointsManager.MemoryReadWriteBreakpoints.SerializableBreakpoints;
        restoredBreakpoints.Should().HaveCount(1);
        restoredBreakpoints.First().ConditionExpression.Should().Be(conditionExpression);
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestComplexConditionalExpression(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        var memory = spice86DependencyInjection.Machine.Memory;
        var state = spice86DependencyInjection.Machine.CpuState;
        
        // Set up test data
        memory.UInt8[0x300] = 0x10;
        memory.UInt8[0x301] = 0x20;
        state.AX = 0x30;
        state.BX = 0x20;
        
        // Create a complex conditional: (byte[0x300] + byte[0x301]) == ax && bx > 0x10
        int triggerCount = 0;
        var breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_WRITE,
            0x300,
            _ => triggerCount++,
            false,
            address => {
                var context = new BreakpointExpressionContext(state, memory, address);
                var parser = new Shared.Emulator.VM.Breakpoint.Expression.ExpressionParser();
                var ast = parser.Parse("(byte[0x300] + byte[0x301]) == ax && bx > 0x10");
                return ast.Evaluate(context) != 0;
            },
            "(byte[0x300] + byte[0x301]) == ax && bx > 0x10");
        
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger (0x10 + 0x20 = 0x30, equals ax, and bx > 0x10)
        memory.UInt8[0x300] = 0x15;
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        state.AX = 0x40;
        
        // This should not trigger
        memory.UInt8[0x300] = 0x16;
        triggerCount.Should().Be(1);
        
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, false);
    }
}
