namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Tests.Fixtures;

using Xunit;

/// <summary>
/// Tests for GDB conditional breakpoints.
/// </summary>
public class GdbConditionalBreakpointTests {
    private readonly BreakpointTestFixture _fixture;
    private State State => _fixture.State;
    private Memory Memory => _fixture.Memory;
    private EmulatorBreakpointsManager BreakpointsManager => _fixture.BreakpointsManager;
    
    public GdbConditionalBreakpointTests() {
        _fixture = new BreakpointTestFixture();
    }
    
    /// <summary>
    /// Creates a GDB command breakpoint handler for testing.
    /// </summary>
    private GdbCommandBreakpointHandler CreateGdbHandler(GdbIo gdbIo) {
        return new GdbCommandBreakpointHandler(
            BreakpointsManager, _fixture.PauseHandler, gdbIo, _fixture.LoggerService, State, Memory);
    }
    
    [Fact]
    public void TestGdbConditionalBreakpointParsing() {
        using GdbIo gdbIo = new GdbIo(10000, _fixture.LoggerService);
        GdbCommandBreakpointHandler gdbBreakpointHandler = CreateGdbHandler(gdbIo);
        
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
        using GdbIo gdbIo = new GdbIo(10001, _fixture.LoggerService);
        GdbCommandBreakpointHandler gdbBreakpointHandler = CreateGdbHandler(gdbIo);
        
        // Set AX to 0x100
        State.AX = 0x100;
        
        // Create a conditional memory read breakpoint at 0x200 that only triggers when ax == 0x100
        string command = "3,200,1;X:ax==0x100";
        AddressBreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command) as AddressBreakPoint;
        
        breakpoint.Should().NotBeNull();
        
        // Create a new breakpoint with a tracking action using the fixture helper
        int triggerCount = 0;
        AddressBreakPoint testBreakpoint = _fixture.CreateConditionalBreakpoint(
            breakpoint!.BreakPointType,
            breakpoint.Address,
            _ => triggerCount++,
            "ax==0x100");
        
        BreakpointsManager.ToggleBreakPoint(testBreakpoint, true);
        
        // This should trigger the breakpoint
        _ = Memory.UInt8[0x200];
        triggerCount.Should().Be(1);
        
        // Change AX so condition fails
        State.AX = 0x200;
        
        // This should not trigger the breakpoint
        _ = Memory.UInt8[0x200];
        triggerCount.Should().Be(1);
        
        BreakpointsManager.ToggleBreakPoint(testBreakpoint, false);
    }
    
    [Fact]
    public void TestGdbConditionalBreakpointWithMemoryAccess() {
        using GdbIo gdbIo = new GdbIo(10002, _fixture.LoggerService);
        GdbCommandBreakpointHandler gdbBreakpointHandler = CreateGdbHandler(gdbIo);
        
        // Set up test data
        Memory.UInt8[0x300] = 0x42;
        
        // Create a conditional memory write breakpoint
        string command = "2,300,1;X:ax==0x42";
        AddressBreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command) as AddressBreakPoint;
        
        breakpoint.Should().NotBeNull();
        
        // Create a new breakpoint with a tracking action using the fixture helper
        int triggerCount = 0;
        AddressBreakPoint testBreakpoint = _fixture.CreateConditionalBreakpoint(
            breakpoint!.BreakPointType,
            breakpoint.Address,
            _ => triggerCount++,
            "ax==0x42");
        
        BreakpointsManager.ToggleBreakPoint(testBreakpoint, true);
        
        // Set ax to trigger condition
        State.AX = 0x42;
        
        // This should trigger (ax == 0x42)
        Memory.UInt8[0x300] = 0x50;
        triggerCount.Should().Be(1);
        
        // Change ax so condition fails
        State.AX = 0x43;
        
        // This should not trigger (condition no longer met)
        Memory.UInt8[0x300] = 0x50;
        triggerCount.Should().Be(1);
        
        BreakpointsManager.ToggleBreakPoint(testBreakpoint, false);
    }
    
    [Fact]
    public void TestGdbBreakpointWithoutCondition() {
        using GdbIo gdbIo = new GdbIo(10003, _fixture.LoggerService);
        GdbCommandBreakpointHandler gdbBreakpointHandler = CreateGdbHandler(gdbIo);
        
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
        using GdbIo gdbIo = new GdbIo(10004, _fixture.LoggerService);
        GdbCommandBreakpointHandler gdbBreakpointHandler = CreateGdbHandler(gdbIo);
        
        // Test with invalid expression - should create unconditional breakpoint
        string command = "0,1000,1;X:invalid_expression((";
        
        BreakPoint? breakpoint = gdbBreakpointHandler.ParseBreakPoint(command);
        
        // Should still create a breakpoint, just without condition
        breakpoint.Should().NotBeNull();
        AddressBreakPoint addressBreakpoint = (AddressBreakPoint)breakpoint!;
        addressBreakpoint.ConditionExpression.Should().BeNull();
    }
}
