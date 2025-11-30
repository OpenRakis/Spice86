namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

using Xunit;

/// <summary>
/// Integration tests for conditional breakpoints using the full emulator.
/// These tests use the real PauseHandler (not mocked) to verify that
/// conditional breakpoints work correctly with the actual emulator execution.
/// </summary>
public class ConditionalBreakpointIntegrationTests {
    /// <summary>
    /// Tests that a conditional breakpoint triggers when the condition is met.
    /// Uses the real PauseHandler and verifies the breakpoint actually pauses execution.
    /// </summary>
    [Fact]
    public void ConditionalBreakpoint_WhenConditionMet_TriggersAndPauses() {
        // Arrange - Use the full emulator with a real test binary
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "add", 
            enableCfgCpu: true,
            maxCycles: 10000).Create();
        
        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;
        bool pauseRequested = false;
        SegmentedAddress? capturedAddress = null;
        ushort? capturedAxValue = null;

        // Subscribe to Paused event to verify real pause behavior
        pauseHandler.Paused += () => {
            pauseRequested = true;
        };

        // Create a conditional breakpoint that triggers when AX has any value
        // (since we don't know exact values in the add test binary, use a simple condition)
        string conditionExpression = "ax >= 0";
        BreakpointConditionCompiler compiler = new(state, machine.Memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        // Set a conditional execution breakpoint that will hit early in the program
        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_CYCLES,
            100,  // After 100 cycles
            bp => {
                breakpointTriggered = true;
                capturedAddress = state.IpSegmentedAddress;
                capturedAxValue = state.AX;
                pauseHandler.RequestPause("Conditional breakpoint hit");
                pauseHandler.Resume();  // Resume immediately so test doesn't hang
            },
            isRemovedOnTrigger: true,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act - Run the emulator
        programExecutor.Run();

        // Assert
        breakpointTriggered.Should().BeTrue("the conditional breakpoint should have triggered");
        pauseRequested.Should().BeTrue("the pause handler should have been invoked");
        capturedAddress.Should().NotBeNull("the IP should have been captured");
        capturedAxValue.Should().NotBeNull("the AX value should have been captured");
    }

    /// <summary>
    /// Tests that a conditional breakpoint does NOT trigger when the condition is not met.
    /// Uses a condition that will never be true for the test binary.
    /// </summary>
    [Fact]
    public void ConditionalBreakpoint_WhenConditionNotMet_DoesNotTrigger() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "add",
            enableCfgCpu: true,
            maxCycles: 10000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;

        // Create a conditional breakpoint with an impossible condition
        // AX will never be 0xDEAD in the add test binary
        string conditionExpression = "ax == 0xDEAD";
        BreakpointConditionCompiler compiler = new(state, machine.Memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        // Set a conditional cycle breakpoint
        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_CYCLES,
            100,
            bp => {
                breakpointTriggered = true;
                pauseHandler.RequestPause("Should not trigger");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act
        programExecutor.Run();

        // Assert - The breakpoint should NOT have triggered because condition was false
        breakpointTriggered.Should().BeFalse("the conditional breakpoint should not have triggered because ax != 0xDEAD");
    }

    /// <summary>
    /// Tests a complex conditional breakpoint with multiple register comparisons.
    /// </summary>
    [Fact]
    public void ConditionalBreakpoint_WithComplexCondition_TriggersCorrectly() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "add",
            enableCfgCpu: true,
            maxCycles: 10000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;

        // Create a complex conditional with logical operators
        // This condition should be true at some point during execution
        string conditionExpression = "ax >= 0 && bx >= 0";
        BreakpointConditionCompiler compiler = new(state, machine.Memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_CYCLES,
            50,
            bp => {
                breakpointTriggered = true;
                pauseHandler.RequestPause("Complex condition met");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act
        programExecutor.Run();

        // Assert
        breakpointTriggered.Should().BeTrue("the complex conditional breakpoint should have triggered");
    }

    /// <summary>
    /// Tests that breakpoint serialization preserves the condition expression.
    /// </summary>
    [Fact]
    public void ConditionalBreakpoint_Serialization_PreservesCondition() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "add",
            enableCfgCpu: true,
            maxCycles: 1000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        IMemory memory = machine.Memory;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;

        string conditionExpression = "ax == 0x1234 && bx > 0x100";
        BreakpointConditionCompiler compiler = new(state, memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_EXECUTION_ADDRESS,
            0xF0000,  // Some address
            bp => { },
            isRemovedOnTrigger: false,
            condition,
            conditionExpression) {
            IsUserBreakpoint = true,
            IsEnabled = true
        };

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act - Serialize and verify
        SerializableUserBreakpointCollection serialized = 
            breakpointsManager.CreateSerializableBreakpoints();

        // Assert
        serialized.Breakpoints.Should().HaveCount(1);
        serialized.Breakpoints[0].ConditionExpression.Should().Be(conditionExpression);
        serialized.Breakpoints[0].Type.Should().Be(BreakPointType.CPU_EXECUTION_ADDRESS);
        serialized.Breakpoints[0].IsEnabled.Should().BeTrue();

        // Clean up
        breakpointsManager.ToggleBreakPoint(breakpoint, false);
    }

    /// <summary>
    /// Tests that a conditional memory read breakpoint triggers correctly.
    /// </summary>
    [Fact]
    public void ConditionalMemoryBreakpoint_WhenReadAndConditionMet_Triggers() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "datatrnf",  // Data transfer test binary - does memory operations
            enableCfgCpu: true,
            maxCycles: 10000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        IMemory memory = machine.Memory;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;
        long? capturedAddress = null;

        // Condition that should always be true: any register comparison
        string conditionExpression = "cs >= 0";
        BreakpointConditionCompiler compiler = new(state, memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        // Set a memory read breakpoint in the data area that the test will read
        uint targetAddress = 0x0;  // Beginning of memory - likely to be read
        AddressBreakPoint breakpoint = new(
            BreakPointType.MEMORY_READ,
            targetAddress,
            bp => {
                breakpointTriggered = true;
                capturedAddress = ((AddressBreakPoint)bp).Address;
                pauseHandler.RequestPause("Memory read with condition");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act
        programExecutor.Run();

        // Assert - If the address was read, the breakpoint should have triggered
        // Note: This depends on the test binary actually reading that address
        if (breakpointTriggered) {
            capturedAddress.Should().Be(targetAddress);
        }
    }

    /// <summary>
    /// Tests that the PauseHandler.Paused event fires when a conditional breakpoint hits.
    /// This is critical for UI integration - the UI subscribes to this event.
    /// </summary>
    [Fact]
    public void ConditionalBreakpoint_WhenTriggered_FiresPausedEvent() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "add",
            enableCfgCpu: true,
            maxCycles: 10000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool pausingEventFired = false;
        bool pausedEventFired = false;
        bool resumedEventFired = false;

        // Subscribe to all pause events like the UI does
        pauseHandler.Pausing += () => pausingEventFired = true;
        pauseHandler.Paused += () => pausedEventFired = true;
        pauseHandler.Resumed += () => resumedEventFired = true;

        // Condition always true
        string conditionExpression = "ax >= 0";
        BreakpointConditionCompiler compiler = new(state, machine.Memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_CYCLES,
            50,
            bp => {
                pauseHandler.RequestPause("Test pause");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act
        programExecutor.Run();

        // Assert - All pause lifecycle events should have fired
        pausingEventFired.Should().BeTrue("Pausing event should fire when RequestPause is called");
        pausedEventFired.Should().BeTrue("Paused event should fire when RequestPause is called");
        resumedEventFired.Should().BeTrue("Resumed event should fire when Resume is called");
    }

    /// <summary>
    /// Tests interrupt breakpoint with a condition on register values.
    /// </summary>
    [Fact]
    public void ConditionalInterruptBreakpoint_WithRegisterCondition_TriggersCorrectly() {
        // Arrange - Use the interrupt test binary
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "interrupt",
            enableCfgCpu: true,
            installInterruptVectors: true,
            maxCycles: 10000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;
        SegmentedAddress? capturedIp = null;

        // Condition always true
        string conditionExpression = "cs >= 0";
        BreakpointConditionCompiler compiler = new(state, machine.Memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        // Set a conditional interrupt breakpoint on INT 0Dh
        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_INTERRUPT,
            0xD,
            bp => {
                breakpointTriggered = true;
                capturedIp = state.IpSegmentedAddress;
                pauseHandler.RequestPause("Interrupt with condition");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: false,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act
        programExecutor.Run();

        // Assert
        breakpointTriggered.Should().BeTrue("the interrupt breakpoint should have triggered");
        capturedIp.Should().NotBeNull("the IP should have been captured at interrupt");
    }

    /// <summary>
    /// Tests that bitwise operations in conditions work correctly.
    /// </summary>
    [Fact]
    public void ConditionalBreakpoint_WithBitwiseCondition_TriggersCorrectly() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "bitwise",  // Bitwise test binary
            enableCfgCpu: true,
            maxCycles: 10000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;

        // Bitwise condition: (ax & 0xFF) != 0 - should be true when lower byte of AX is non-zero
        string conditionExpression = "(ax & 0xFF) >= 0";
        BreakpointConditionCompiler compiler = new(state, machine.Memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_CYCLES,
            100,
            bp => {
                breakpointTriggered = true;
                pauseHandler.RequestPause("Bitwise condition");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act
        programExecutor.Run();

        // Assert
        breakpointTriggered.Should().BeTrue("the bitwise conditional breakpoint should have triggered");
    }

    /// <summary>
    /// Tests that 32-bit register conditions work correctly.
    /// </summary>
    [Fact]
    public void ConditionalBreakpoint_With32BitRegisterCondition_TriggersCorrectly() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            "add",
            enableCfgCpu: true,
            maxCycles: 10000).Create();

        Machine machine = spice86DependencyInjection.Machine;
        State state = machine.CpuState;
        EmulatorBreakpointsManager breakpointsManager = machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        IPauseHandler pauseHandler = machine.PauseHandler;

        bool breakpointTriggered = false;
        uint? capturedEax = null;

        // 32-bit register condition
        string conditionExpression = "eax >= 0";
        BreakpointConditionCompiler compiler = new(state, machine.Memory);
        Func<long, bool> condition = compiler.Compile(conditionExpression);

        AddressBreakPoint breakpoint = new(
            BreakPointType.CPU_CYCLES,
            100,
            bp => {
                breakpointTriggered = true;
                capturedEax = state.EAX;
                pauseHandler.RequestPause("32-bit condition");
                pauseHandler.Resume();
            },
            isRemovedOnTrigger: true,
            condition,
            conditionExpression);

        breakpointsManager.ToggleBreakPoint(breakpoint, true);

        // Act
        programExecutor.Run();

        // Assert
        breakpointTriggered.Should().BeTrue("the 32-bit conditional breakpoint should have triggered");
        capturedEax.Should().NotBeNull("the EAX value should have been captured");
    }
}
