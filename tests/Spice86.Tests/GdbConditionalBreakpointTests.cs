namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Tests for GDB conditional breakpoints.
/// </summary>
public class GdbConditionalBreakpointTests {
    private readonly State _state;
    private readonly Memory _memory;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly ILoggerService _loggerService;
    private readonly AddressReadWriteBreakpoints _memoryBreakpoints;
    private readonly AddressReadWriteBreakpoints _ioBreakpoints;
    
    public GdbConditionalBreakpointTests() {
        // Create State directly
        _state = new State(CpuModel.INTEL_80286);
        
        // Create Memory with required dependencies
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        _memoryBreakpoints = new AddressReadWriteBreakpoints();
        _ioBreakpoints = new AddressReadWriteBreakpoints();
        A20Gate a20Gate = new(enabled: false);
        _memory = new Memory(_memoryBreakpoints, ram, a20Gate, initializeResetVector: true);
        
        // Create mocked dependencies
        _pauseHandler = Substitute.For<IPauseHandler>();
        _loggerService = Substitute.For<ILoggerService>();
        
        // Create breakpoints manager
        _emulatorBreakpointsManager = new EmulatorBreakpointsManager(_pauseHandler, _state, _memory, _memoryBreakpoints, _ioBreakpoints);
    }
    
    [Fact]
    public void TestGdbConditionalBreakpointParsing() {
        using GdbIo gdbIo = new GdbIo(10000, _loggerService);
        
        GdbCommandBreakpointHandler gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            _emulatorBreakpointsManager, _pauseHandler, gdbIo, _loggerService, _state, _memory);
        
        // Test parsing a conditional breakpoint command
        // Format: type,address,kind;X:condition
        // Type 0 = execution breakpoint, address 0x1000, kind 1, condition: ax == 0x100
        string command = "0,1000,1;X:ax==0x100";
        
        BreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command);
        
        breakpoint.Should().NotBeNull();
        breakpoint.Should().BeOfType<AddressBreakPoint>();
        
        AddressBreakPoint addressBreakpoint = (AddressBreakPoint)breakpoint!;
        addressBreakpoint.Address.Should().Be(0x1000);
        addressBreakpoint.BreakPointType.Should().Be(BreakPointType.CPU_EXECUTION_ADDRESS);
        addressBreakpoint.ConditionExpression.Should().Be("ax==0x100");
    }
    
    [Fact]
    public void TestGdbConditionalBreakpointExecution() {
        using GdbIo gdbIo = new GdbIo(10001, _loggerService);
        
        GdbCommandBreakpointHandler gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            _emulatorBreakpointsManager, _pauseHandler, gdbIo, _loggerService, _state, _memory);
        
        // Set AX to 0x100
        _state.AX = 0x100;
        
        // Create a conditional memory read breakpoint at 0x200 that only triggers when ax == 0x100
        string command = "3,200,1;X:ax==0x100";
        AddressBreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command) as AddressBreakPoint;
        
        breakpoint.Should().NotBeNull();
        
        // Create a new breakpoint with a tracking action
        // Compile the condition using BreakpointConditionCompiler
        BreakpointConditionCompiler compiler = new BreakpointConditionCompiler(_state, _memory);
        Func<long, bool> condition = compiler.Compile("ax==0x100");
        
        int triggerCount = 0;
        AddressBreakPoint testBreakpoint = new AddressBreakPoint(
            breakpoint!.BreakPointType, 
            breakpoint.Address, 
            _ => triggerCount++, 
            false,
            condition,
            "ax==0x100");
        
        _emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, true);
        
        // This should trigger the breakpoint
        _ = _memory.UInt8[0x200];
        triggerCount.Should().Be(1);
        
        // Change AX so condition fails
        _state.AX = 0x200;
        
        // This should not trigger the breakpoint
        _ = _memory.UInt8[0x200];
        triggerCount.Should().Be(1);
        
        _emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, false);
    }
    
    [Fact]
    public void TestGdbConditionalBreakpointWithMemoryAccess() {
        using GdbIo gdbIo = new GdbIo(10002, _loggerService);
        
        GdbCommandBreakpointHandler gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            _emulatorBreakpointsManager, _pauseHandler, gdbIo, _loggerService, _state, _memory);
        
        // Set up test data
        _memory.UInt8[0x300] = 0x42;
        
        // Create a conditional memory write breakpoint
        // Note: Avoid memory access in conditions for now as it triggers recursive breakpoints
        string command = "2,300,1;X:ax==0x42";
        AddressBreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command) as AddressBreakPoint;
        
        breakpoint.Should().NotBeNull();
        
        // Compile the condition using BreakpointConditionCompiler
        BreakpointConditionCompiler compiler = new BreakpointConditionCompiler(_state, _memory);
        Func<long, bool> condition = compiler.Compile("ax==0x42");
        
        // Create a new breakpoint with a tracking action
        int triggerCount = 0;
        AddressBreakPoint testBreakpoint = new AddressBreakPoint(
            breakpoint!.BreakPointType,
            breakpoint.Address,
            _ => triggerCount++,
            false,
            condition,
            "ax==0x42");
        
        _emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, true);
        
        // Set ax to trigger condition
        _state.AX = 0x42;
        
        // This should trigger (ax == 0x42)
        _memory.UInt8[0x300] = 0x50;
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        _state.AX = 0x43;
        
        // This should not trigger (condition no longer met)
        _memory.UInt8[0x300] = 0x50;
        triggerCount.Should().Be(1);
        
        _emulatorBreakpointsManager.ToggleBreakPoint(testBreakpoint, false);
    }
    
    [Fact]
    public void TestGdbBreakpointWithoutCondition() {
        using GdbIo gdbIo = new GdbIo(10003, _loggerService);
        
        GdbCommandBreakpointHandler gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            _emulatorBreakpointsManager, _pauseHandler, gdbIo, _loggerService, _state, _memory);
        
        // Test unconditional breakpoint (existing functionality)
        string command = "0,1000,1";
        
        BreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command);
        
        breakpoint.Should().NotBeNull();
        breakpoint.Should().BeOfType<AddressBreakPoint>();
        
        AddressBreakPoint addressBreakpoint = (AddressBreakPoint)breakpoint!;
        addressBreakpoint.Address.Should().Be(0x1000);
        addressBreakpoint.ConditionExpression.Should().BeNull();
    }
    
    [Fact]
    public void TestGdbConditionalBreakpointWithInvalidExpression() {
        using GdbIo gdbIo = new GdbIo(10004, _loggerService);
        
        GdbCommandBreakpointHandler gdbBreakpointHandler = new GdbCommandBreakpointHandler(
            _emulatorBreakpointsManager, _pauseHandler, gdbIo, _loggerService, _state, _memory);
        
        // Test with invalid expression - should create unconditional breakpoint
        string command = "0,1000,1;X:invalid_expression((";
        
        BreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command);
        
        // Should still create a breakpoint, just without condition
        breakpoint.Should().NotBeNull();
        AddressBreakPoint addressBreakpoint = (AddressBreakPoint)breakpoint!;
        addressBreakpoint.ConditionExpression.Should().BeNull();
    }
}
