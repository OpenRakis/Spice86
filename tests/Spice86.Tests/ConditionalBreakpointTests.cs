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
        
        // Create a conditional breakpoint that only triggers when ax == 0x100
        // Note: Avoid memory access in conditions for now as it triggers recursive breakpoints
        string conditionExpression = "ax == 0x100";
        var compiler = new BreakpointConditionCompiler(state, memory);
        var condition = compiler.Compile(conditionExpression);
        
        int triggerCount = 0;
        var breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_READ,
            0x100,
            _ => triggerCount++,
            false,
            condition,
            conditionExpression);
        
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger the breakpoint (ax is 0x100)
        _ = memory.UInt8[0x100];
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        state.AX = 0x200;
        
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
        var compiler = new BreakpointConditionCompiler(state, memory);
        var condition = compiler.Compile(conditionExpression);
        
        var breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_READ,
            0x200,
            _ => { },
            false,
            condition,
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
        
        // Create a complex conditional: ax == 0x30 && bx > 0x10
        // Note: Avoid memory access in conditions for now as it triggers recursive breakpoints
        string conditionExpression = "ax == 0x30 && bx > 0x10";
        var compiler = new BreakpointConditionCompiler(state, memory);
        var condition = compiler.Compile(conditionExpression);
        
        int triggerCount = 0;
        var breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_WRITE,
            0x300,
            _ => triggerCount++,
            false,
            condition,
            conditionExpression);
        
        emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger (ax == 0x30 and bx > 0x10)
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
