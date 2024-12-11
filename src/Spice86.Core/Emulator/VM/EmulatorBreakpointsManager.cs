namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// A class for managing breakpoints in the emulator.
/// </summary>
public sealed class EmulatorBreakpointsManager {
    private readonly BreakPointHolder _cycleBreakPoints;
    private readonly BreakPointHolder _executionBreakPoints;
    private readonly State _state;
    private readonly IPauseHandler _pauseHandler;
    private readonly MemoryBreakpoints _memoryBreakpoints;
    private BreakPoint? _machineStopBreakPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatorBreakpointsManager"/> class.
    /// </summary>
    /// <param name="memoryBreakpoints">The class that holds breakpoints based on memory access.</param>
    /// <param name="pauseHandler">The object responsible for pausing and resuming the emulation.</param>
    /// <param name="state">The CPU registers and flags.</param>
    public EmulatorBreakpointsManager(MemoryBreakpoints memoryBreakpoints, IPauseHandler pauseHandler, State state) {
        _state = state;
        _memoryBreakpoints = memoryBreakpoints;
        _cycleBreakPoints = new();
        _executionBreakPoints = new();
        _pauseHandler = pauseHandler;
    }

    /// <summary>
    /// Checks the current breakpoints.
    /// </summary>
    public void CheckBreakPoint() => CheckBreakPoints();

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
            case BreakPointType.EXECUTION:
                _executionBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.CYCLES:
                _cycleBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.MACHINE_STOP:
                _machineStopBreakPoint = breakPoint;
                break;
            default:
                _memoryBreakpoints.ToggleBreakPoint(breakPoint, on);
                break;
        }
    }

    /// <summary>
    /// Checks the current breakpoints and triggers them if necessary.
    /// </summary>
    private void CheckBreakPoints() {
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