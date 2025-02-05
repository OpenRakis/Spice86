namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// A class for managing breakpoints in the emulator.
/// </summary>
public sealed class EmulatorBreakpointsManager {
    private readonly BreakPointHolder _cycleBreakPoints;
    private readonly BreakPointHolder _executionBreakPoints;
    private readonly State _state;
    private readonly IPauseHandler _pauseHandler;
    private BreakPoint? _machineStopBreakPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatorBreakpointsManager"/> class.
    /// </summary>
    /// <param name="pauseHandler">The object responsible for pausing and resuming the emulation.</param>
    /// <param name="state">The CPU registers and flags.</param>
    public EmulatorBreakpointsManager(IPauseHandler pauseHandler, State state) {
        _state = state;
        MemoryReadWriteBreakpoints = new();
        IoReadWriteBreakpoints = new();
        InterruptBreakPoints = new();
        _cycleBreakPoints = new();
        _executionBreakPoints = new();
        _pauseHandler = pauseHandler;
    }
    public AddressReadWriteBreakpoints MemoryReadWriteBreakpoints { get; }
    public AddressReadWriteBreakpoints IoReadWriteBreakpoints { get; }
    public BreakPointHolder InterruptBreakPoints { get; }

    /// <summary>
    /// Called when the machine stops.
    /// </summary>
    public void OnMachineStop() {
        if (_machineStopBreakPoint is not null) {
            _machineStopBreakPoint.Trigger();
            _pauseHandler.WaitIfPaused();
        }
    }

    /// <summary>
    /// Toggles a breakpoint on or off.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to toggle.</param>
    /// <param name="on">True to turn the breakpoint on, false to turn it off.</param>
    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        BreakPointType breakPointType = breakPoint.BreakPointType;
        switch (breakPointType) {
            case BreakPointType.CPU_EXECUTION_ADDRESS:
                _executionBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.CPU_CYCLES:
                _cycleBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.CPU_INTERRUPT:
                InterruptBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.MACHINE_STOP:
                _machineStopBreakPoint = breakPoint;
                break;
            case BreakPointType.MEMORY_READ:
                MemoryReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.READ, on);
                break;
            case BreakPointType.MEMORY_WRITE:
                MemoryReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.WRITE, on);
                break;
            case BreakPointType.MEMORY_ACCESS:
                MemoryReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.ACCESS, on);
                break;
            case BreakPointType.IO_READ:
                IoReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.READ, on);
                break;
            case BreakPointType.IO_WRITE:
                IoReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.WRITE, on);
                break;
            case BreakPointType.IO_ACCESS:
                IoReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.ACCESS, on);
                break;
        }
    }

    /// <summary>
    /// Checks the current breakpoints and triggers them if necessary.
    /// </summary>
    public void CheckExecutionBreakPoints() {
        if (!_executionBreakPoints.IsEmpty) {
            uint address = _state.IpPhysicalAddress;
            _executionBreakPoints.TriggerMatchingBreakPoints(address);
        }

        if (!_cycleBreakPoints.IsEmpty) {
            long cycles = _state.Cycles;
            _cycleBreakPoints.TriggerMatchingBreakPoints(cycles);
        }
    }
}