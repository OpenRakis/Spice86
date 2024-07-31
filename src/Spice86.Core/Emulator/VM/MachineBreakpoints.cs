namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// A class for managing breakpoints in the machine.
/// </summary>
public sealed class MachineBreakpoints {
    /// <summary>
    /// A holder for cycle breakpoints.
    /// </summary>
    private readonly BreakPointHolder _cycleBreakPoints;

    /// <summary>
    /// A holder for execution breakpoints.
    /// </summary>
    private readonly BreakPointHolder _executionBreakPoints;

    /// <summary>
    /// The memory bus.
    /// </summary>
    private readonly IMemory _memory;

    /// <summary>
    /// The CPU State.
    /// </summary>
    private readonly State _state;

    /// <summary>
    /// The machine stop breakpoint.
    /// </summary>
    private BreakPoint? _machineStopBreakPoint;

    /// <summary>
    /// The pause handler for the machine.
    /// </summary>
    private readonly IPauseHandler _pauseHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="MachineBreakpoints"/> class.
    /// </summary>
    /// <param name="pauseHandler">The object responsible for pausing and resuming the emulation.</param>
    /// <param name="cycleBreakPoints">Holds breakpoints based on CPU cycles.</param>
    /// <param name="executionBreakPoints">Holds breakpoints based on the current instruction to be executed by the CPU.</param>
    /// <param name="memory">The IBM PC memory bus</param>
    /// <param name="state">The CPU state</param>
    public MachineBreakpoints(IPauseHandler pauseHandler, BreakPointHolder cycleBreakPoints, BreakPointHolder executionBreakPoints, IMemory memory, State state) {
        _state = state;
        _memory = memory;
        _cycleBreakPoints = cycleBreakPoints;
        _executionBreakPoints = executionBreakPoints;
        _pauseHandler = pauseHandler;
    }

    /// <summary>
    /// Checks the current breakpoints.
    /// </summary>
    public void CheckBreakPoint() {
        CheckBreakPoints();
    }

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
    public void ToggleBreakPoint(BreakPoint? breakPoint, bool on) {
        if (breakPoint is null) {
            return;
        }
        BreakPointType? breakPointType = breakPoint.BreakPointType;
        if (breakPointType == BreakPointType.EXECUTION) {
            _executionBreakPoints.ToggleBreakPoint(breakPoint, on);
        } else if (breakPointType == BreakPointType.CYCLES) {
            _cycleBreakPoints.ToggleBreakPoint(breakPoint, on);
        } else if (breakPointType == BreakPointType.MACHINE_STOP) {
            _machineStopBreakPoint = breakPoint;
        } else {
            _memory.MemoryBreakpoints.ToggleBreakPoint(breakPoint, on);
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