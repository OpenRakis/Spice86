namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;

public sealed class MachineBreakpoints : IDisposable {
    private readonly BreakPointHolder _cycleBreakPoints = new();

    private readonly BreakPointHolder _executionBreakPoints = new();

    private readonly Memory _memory;

    private readonly State _state;

    private BreakPoint? _machineStopBreakPoint;
    private bool _disposed;

    public MachineBreakpoints(Machine machine) {
        _state = machine.Cpu.State;
        _memory = machine.Memory;
    }

    public void CheckBreakPoint() {
        CheckBreakPoints();
        PauseHandler.WaitIfPaused();
    }

    public PauseHandler PauseHandler { get; } = new();

    public void OnMachineStop() {
        if (_machineStopBreakPoint is not null) {
            _machineStopBreakPoint.Trigger();
            PauseHandler.WaitIfPaused();
        }
    }

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

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}