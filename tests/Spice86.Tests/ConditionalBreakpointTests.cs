namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Tests for conditional breakpoints with AST-based expressions.
/// </summary>
public class ConditionalBreakpointTests {
    private readonly State _state;
    private readonly Memory _memory;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    
    public ConditionalBreakpointTests() {
        // Create State directly
        _state = new State(CpuModel.INTEL_80286);
        
        // Create Memory with required dependencies
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        A20Gate a20Gate = new(enabled: false);
        _memory = new Memory(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);
        
        // Create breakpoints manager
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();
        _emulatorBreakpointsManager = new EmulatorBreakpointsManager(pauseHandler, _state, _memory, memoryBreakpoints, ioBreakpoints);
    }
    
    [Fact]
    public void TestConditionalMemoryBreakpoint() {
        // Set up test data
        _memory.UInt8[0x100] = 0x42;
        _state.AX = 0x100;
        
        // Create a conditional breakpoint that only triggers when ax == 0x100
        // Note: Avoid memory access in conditions for now as it triggers recursive breakpoints
        string conditionExpression = "ax == 0x100";
        BreakpointConditionCompiler compiler = new BreakpointConditionCompiler(_state, _memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);
        
        int triggerCount = 0;
        AddressBreakPoint breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_READ,
            0x100,
            _ => triggerCount++,
            false,
            condition,
            conditionExpression);
        
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger the breakpoint (ax is 0x100)
        _ = _memory.UInt8[0x100];
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        _state.AX = 0x200;
        
        // This should not trigger the breakpoint
        _ = _memory.UInt8[0x100];
        triggerCount.Should().Be(1);
        
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, false);
    }
    
    [Fact]
    public void TestConditionalBreakpointSerialization() {
        // Set up test data
        _memory.UInt8[0x200] = 0x55;
        _state.AX = 0x10;
        
        // Create a conditional breakpoint with an expression
        string conditionExpression = "ax == 0x10";
        BreakpointConditionCompiler compiler = new BreakpointConditionCompiler(_state, _memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);
        
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
        
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // Serialize
        var serialized = _emulatorBreakpointsManager.CreateSerializableBreakpoints();
        
        // Verify the condition expression was serialized
        serialized.Breakpoints.Should().HaveCount(1);
        serialized.Breakpoints[0].ConditionExpression.Should().Be(conditionExpression);
        serialized.Breakpoints[0].Trigger.Should().Be(0x200);
        serialized.Breakpoints[0].Type.Should().Be(BreakPointType.MEMORY_READ);
        serialized.Breakpoints[0].IsEnabled.Should().BeTrue();
        
        // Remove the breakpoint
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, false);
        
        // Restore from serialized data
        _emulatorBreakpointsManager.RestoreBreakpoints(serialized);
        
        // Test that the restored breakpoint works correctly
        // We need to replace the breakpoint's onReached action since we can't serialize that
        // In practice, this is handled by the BreakpointsViewModel
        IEnumerable<AddressBreakPoint> restoredBreakpoints = _emulatorBreakpointsManager.MemoryReadWriteBreakpoints.SerializableBreakpoints;
        restoredBreakpoints.Should().HaveCount(1);
        restoredBreakpoints.First().ConditionExpression.Should().Be(conditionExpression);
    }
    
    [Fact]
    public void TestComplexConditionalExpression() {
        // Set up test data
        _memory.UInt8[0x300] = 0x10;
        _memory.UInt8[0x301] = 0x20;
        _state.AX = 0x30;
        _state.BX = 0x20;
        
        // Create a complex conditional: ax == 0x30 && bx > 0x10
        // Note: Avoid memory access in conditions for now as it triggers recursive breakpoints
        string conditionExpression = "ax == 0x30 && bx > 0x10";
        BreakpointConditionCompiler compiler = new BreakpointConditionCompiler(_state, _memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);
        
        int triggerCount = 0;
        AddressBreakPoint breakpoint = new AddressBreakPoint(
            BreakPointType.MEMORY_WRITE,
            0x300,
            _ => triggerCount++,
            false,
            condition,
            conditionExpression);
        
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, true);
        
        // This should trigger (ax == 0x30 and bx > 0x10)
        _memory.UInt8[0x300] = 0x15;
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        _state.AX = 0x40;
        
        // This should not trigger
        _memory.UInt8[0x300] = 0x16;
        triggerCount.Should().Be(1);
        
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, false);
    }
}
