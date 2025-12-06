namespace Spice86.Tests.Fixtures;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

/// <summary>
/// Shared fixture for breakpoint tests that creates State, Memory, and EmulatorBreakpointsManager.
/// Only ILoggerService is mocked; all other components use real implementations.
/// This fixture avoids referencing the Machine class.
/// </summary>
public class BreakpointTestFixture : IDisposable {
    /// <summary>
    /// The CPU state for testing.
    /// </summary>
    public State State { get; }

    /// <summary>
    /// The memory instance for testing.
    /// </summary>
    public Memory Memory { get; }

    /// <summary>
    /// The breakpoints manager for testing.
    /// </summary>
    public EmulatorBreakpointsManager BreakpointsManager { get; }

    /// <summary>
    /// The real pause handler (not mocked).
    /// </summary>
    public PauseHandler PauseHandler { get; }

    /// <summary>
    /// The mocked logger service (only ILoggerService is mocked).
    /// </summary>
    public ILoggerService LoggerService { get; }

    /// <summary>
    /// Memory breakpoints for read/write tracking.
    /// </summary>
    public AddressReadWriteBreakpoints MemoryBreakpoints { get; }

    /// <summary>
    /// IO breakpoints for port tracking.
    /// </summary>
    public AddressReadWriteBreakpoints IoBreakpoints { get; }

    /// <summary>
    /// Creates a new test fixture with all required components.
    /// Only ILoggerService is mocked; all other components are real implementations.
    /// </summary>
    public BreakpointTestFixture() {
        // Create State directly
        State = new State(CpuModel.INTEL_80286);

        // Create Memory with required dependencies
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        MemoryBreakpoints = new AddressReadWriteBreakpoints();
        IoBreakpoints = new AddressReadWriteBreakpoints();
        A20Gate a20Gate = new(enabled: false);
        Memory = new Memory(MemoryBreakpoints, ram, a20Gate, initializeResetVector: true);

        // Only ILoggerService is mocked
        LoggerService = Substitute.For<ILoggerService>();

        // Use real PauseHandler
        PauseHandler = new PauseHandler(LoggerService);

        // Create breakpoints manager with real components
        BreakpointsManager = new EmulatorBreakpointsManager(PauseHandler, State, Memory, MemoryBreakpoints, IoBreakpoints);
    }

    /// <summary>
    /// Disposes of the fixture resources.
    /// </summary>
    public void Dispose() {
        PauseHandler.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Compiles a condition expression for use with breakpoints.
    /// </summary>
    /// <param name="expression">The expression to compile.</param>
    /// <returns>A function that evaluates the condition.</returns>
    public Func<long, bool> CompileCondition(string expression) {
        BreakpointConditionCompiler compiler = new(State, Memory);
        return compiler.Compile(expression);
    }

    /// <summary>
    /// Creates an address breakpoint with a condition.
    /// </summary>
    /// <param name="type">The breakpoint type.</param>
    /// <param name="address">The address to break at.</param>
    /// <param name="onReached">Action to execute when breakpoint is hit.</param>
    /// <param name="conditionExpression">The condition expression.</param>
    /// <param name="isRemovedOnTrigger">Whether to remove after first trigger.</param>
    /// <returns>A new AddressBreakPoint.</returns>
    public AddressBreakPoint CreateConditionalBreakpoint(
        BreakPointType type,
        long address,
        Action<BreakPoint> onReached,
        string conditionExpression,
        bool isRemovedOnTrigger = false) {
        Func<long, bool> condition = CompileCondition(conditionExpression);
        return new AddressBreakPoint(type, address, onReached, isRemovedOnTrigger, condition, conditionExpression);
    }
}