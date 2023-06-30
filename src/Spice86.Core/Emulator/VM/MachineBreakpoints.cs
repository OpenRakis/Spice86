namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

/// <summary>
/// A class for managing breakpoints in the machine.
/// </summary>
public sealed class MachineBreakpoints : IDisposable {
    /// <summary>
    /// A holder for cycle breakpoints.
    /// </summary>
    private readonly BreakPointHolder _cycleBreakPoints = new();

    /// <summary>
    /// A holder for execution breakpoints.
    /// </summary>
    private readonly BreakPointHolder _executionBreakPoints = new();

    /// <summary>
    /// The memory associated with the machine.
    /// </summary>
    private readonly IMemory _memory;

    /// <summary>
    /// The state associated with the machine.
    /// </summary>
    private readonly State _state;

    /// <summary>
    /// The machine stop breakpoint.
    /// </summary>
    private BreakPoint? _machineStopBreakPoint;

    /// <summary>
    /// True if the object has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MachineBreakpoints"/> class.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public MachineBreakpoints(Machine machine, ILoggerService loggerService) {
        _state = machine.Cpu.State;
        _memory = machine.Memory;
        PauseHandler = new(
            loggerService,
            machine.Gui);
    }

    /// <summary>
    /// Checks the current breakpoints.
    /// </summary>
    public void CheckBreakPoint() {
        CheckBreakPoints();
        PauseHandler.WaitIfPaused();
    }

    /// <summary>
    /// The pause handler associated with the breakpoints.
    /// </summary>
    public PauseHandler PauseHandler { get; }

    /// <summary>
    /// Called when the machine stops.
    /// </summary>
    public void OnMachineStop() {
        if (_machineStopBreakPoint is not null) {
            _machineStopBreakPoint.Trigger();
            PauseHandler.WaitIfPaused();
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
            _memory.ToggleBreakPoint(breakPoint, on);
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

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                PauseHandler.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}