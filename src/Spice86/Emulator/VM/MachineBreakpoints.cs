namespace Spice86.Emulator.VM;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM.Breakpoint;
using Spice86.Emulator.Memory;

public class MachineBreakpoints {
    private readonly BreakPointHolder _cycleBreakPoints = new();

    private readonly BreakPointHolder _executionBreakPoints = new();

    private readonly Memory _memory;

    private readonly PauseHandler _pauseHandler = new();

    private readonly State _state;

    private BreakPoint? _machineStopBreakPoint;

    public MachineBreakpoints(Machine machine) {
        _state = machine.GetCpu().GetState();
        _memory = machine.GetMemory();
    }

    public void CheckBreakPoint() {
        CheckBreakPoints();
        _pauseHandler.WaitIfPaused();
    }

    public PauseHandler GetPauseHandler() {
        return _pauseHandler;
    }

    public void OnMachineStop() {
        if (_machineStopBreakPoint is not null) {
            _machineStopBreakPoint.Trigger();
            _pauseHandler.WaitIfPaused();
        }
    }

    public void ToggleBreakPoint(BreakPoint? breakPoint, bool on) {
        if (breakPoint is null) {
            return;
        }
        BreakPointType? breakPointType = breakPoint.GetBreakPointType();
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
        if (!_executionBreakPoints.IsEmpty()) {
            uint address = _state.GetIpPhysicalAddress();
            _executionBreakPoints.TriggerMatchingBreakPoints(address);
        }

        if (!_cycleBreakPoints.IsEmpty()) {
            long cycles = _state.GetCycles();
            _cycleBreakPoints.TriggerMatchingBreakPoints(cycles);
        }
    }
}