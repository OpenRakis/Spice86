namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Tests for GDB conditional breakpoints.
/// </summary>
public class GdbConditionalBreakpointTests {
    public static IEnumerable<object[]> GetCfgCpuConfigurations() {
        yield return new object[] { false };
        yield return new object[] { true };
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestGdbConditionalBreakpointParsing(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        var state = spice86DependencyInjection.Machine.CpuState;
        var memory = spice86DependencyInjection.Machine.Memory;
        var pauseHandler = spice86DependencyInjection.Machine.PauseHandler;
        var loggerService = Substitute.For<ILoggerService>();
        using var gdbIo = new GdbIo(10000, loggerService);
        var emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        
        var gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            emulatorBreakpointsManager, pauseHandler, gdbIo, loggerService, state, memory);
        
        // Test parsing a conditional breakpoint command
        // Format: type,address,kind;X:condition
        // Type 0 = execution breakpoint, address 0x1000, kind 1, condition: ax == 0x100
        string command = "0,1000,1;X:ax==0x100";
        
        var breakpoint = gdbBreakpointHandler.ParseBreakPoint(command);
        
        breakpoint.Should().NotBeNull();
        breakpoint.Should().BeOfType<AddressBreakPoint>();
        
        var addressBreakpoint = (AddressBreakPoint)breakpoint!;
        addressBreakpoint.Address.Should().Be(0x1000);
        addressBreakpoint.BreakPointType.Should().Be(BreakPointType.CPU_EXECUTION_ADDRESS);
        addressBreakpoint.ConditionExpression.Should().Be("ax==0x100");
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestGdbConditionalBreakpointExecution(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        var state = spice86DependencyInjection.Machine.CpuState;
        var memory = spice86DependencyInjection.Machine.Memory;
        var pauseHandler = spice86DependencyInjection.Machine.PauseHandler;
        var loggerService = Substitute.For<ILoggerService>();
        using var gdbIo = new GdbIo(10001, loggerService);
        var emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        
        var gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            emulatorBreakpointsManager, pauseHandler, gdbIo, loggerService, state, memory);
        
        // Set AX to 0x100
        state.AX = 0x100;
        
        // Create a conditional memory read breakpoint at 0x200 that only triggers when ax == 0x100
        string command = "3,200,1;X:ax==0x100";
        var breakpoint = gdbBreakpointHandler.ParseBreakPoint(command) as AddressBreakPoint;
        
        breakpoint.Should().NotBeNull();
        
        // Create a new breakpoint with a tracking action
        // Compile the condition using BreakpointConditionCompiler
        var compiler = new BreakpointConditionCompiler(state, memory);
        var condition = compiler.Compile("ax==0x100");
        
        int triggerCount = 0;
        var testBreakpoint = new AddressBreakPoint(
            breakpoint!.BreakPointType, 
            breakpoint.Address, 
            _ => triggerCount++, 
            false,
            condition,
            "ax==0x100");
        
        emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, true);
        
        // This should trigger the breakpoint
        _ = memory.UInt8[0x200];
        triggerCount.Should().Be(1);
        
        // Change AX so condition fails
        state.AX = 0x200;
        
        // This should not trigger the breakpoint
        _ = memory.UInt8[0x200];
        triggerCount.Should().Be(1);
        
        emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, false);
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestGdbConditionalBreakpointWithMemoryAccess(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        var state = spice86DependencyInjection.Machine.CpuState;
        var memory = spice86DependencyInjection.Machine.Memory;
        var pauseHandler = spice86DependencyInjection.Machine.PauseHandler;
        var loggerService = Substitute.For<ILoggerService>();
        using var gdbIo = new GdbIo(10002, loggerService);
        var emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        
        var gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            emulatorBreakpointsManager, pauseHandler, gdbIo, loggerService, state, memory);
        
        // Set up test data
        memory.UInt8[0x300] = 0x42;
        
        // Create a conditional memory write breakpoint
        // Note: Avoid memory access in conditions for now as it triggers recursive breakpoints
        string command = "2,300,1;X:ax==0x42";
        if (gdbBreakpointHandler.ParseBreakPoint(command) is not AddressBreakPoint breakpoint) {
            Xunit.Assert.True(false, "Failed to parse address breakpoint.");
        }
        
        // Compile the condition using BreakpointConditionCompiler
        var compiler = new BreakpointConditionCompiler(state, memory);
        var condition = compiler.Compile("ax==0x42");
        
        // Create a new breakpoint with a tracking action
        int triggerCount = 0;
        var testBreakpoint = new AddressBreakPoint(
            breakpoint.BreakPointType,
            breakpoint.Address,
            _ => triggerCount++,
            false,
            condition,
            "ax==0x42");
        
        emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, true);
        
        // Set ax to trigger condition
        state.AX = 0x42;
        
        // This should trigger (ax == 0x42)
        memory.UInt8[0x300] = 0x50;
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        state.AX = 0x43;
        
        // This should not trigger (condition no longer met)
        memory.UInt8[0x300] = 0x50;
        triggerCount.Should().Be(1);
        
        emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, false);
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestGdbBreakpointWithoutCondition(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        var state = spice86DependencyInjection.Machine.CpuState;
        var memory = spice86DependencyInjection.Machine.Memory;
        var pauseHandler = spice86DependencyInjection.Machine.PauseHandler;
        var loggerService = Substitute.For<ILoggerService>();
        using var gdbIo = new GdbIo(10003, loggerService);
        var emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        
        var gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            emulatorBreakpointsManager, pauseHandler, gdbIo, loggerService, state, memory);
        
        // Test unconditional breakpoint (existing functionality)
        string command = "0,1000,1";
        
        var breakpoint = gdbBreakpointHandler.ParseBreakPoint(command);
        
        breakpoint.Should().NotBeNull();
        breakpoint.Should().BeOfType<AddressBreakPoint>();
        
        var addressBreakpoint = (AddressBreakPoint)breakpoint!;
        addressBreakpoint.Address.Should().Be(0x1000);
        addressBreakpoint.ConditionExpression.Should().BeNull();
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestGdbConditionalBreakpointWithInvalidExpression(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        var state = spice86DependencyInjection.Machine.CpuState;
        var memory = spice86DependencyInjection.Machine.Memory;
        var pauseHandler = spice86DependencyInjection.Machine.PauseHandler;
        var loggerService = Substitute.For<ILoggerService>();
        using var gdbIo = new GdbIo(10004, loggerService);
        var emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        
        var gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            emulatorBreakpointsManager, pauseHandler, gdbIo, loggerService, state, memory);
        
        // Test with invalid expression - should create unconditional breakpoint
        string command = "0,1000,1;X:invalid_expression((";
        
        var breakpoint = gdbBreakpointHandler.ParseBreakPoint(command);
        
        // Should still create a breakpoint, just without condition
        breakpoint.Should().NotBeNull();
        var addressBreakpoint = (AddressBreakPoint)breakpoint!;
        addressBreakpoint.ConditionExpression.Should().BeNull();
    }
}
