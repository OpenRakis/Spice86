namespace Spice86.Emulator.VM;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM.Breakpoint;
using Spice86.Emulator.Memory;

public class MachineBreakpoints {
    private readonly BreakPointHolder _cycleBreakPoints = new();

    private readonly BreakPointHolder _executionBreakPoints = new();

    private readonly Memory _memory;

    private readonly State _state;

    private BreakPoint? _machineStopBreakPoint;

    public MachineBreakpoints(Machine machine) {
        _state = machine.Cpu.GetState();
        _memory = machine.Memory;
    }

    public void CheckBreakPoint() {
        CheckBreakPoints();
        PauseHandler.WaitIfPaused();
    }

    public PauseHandler PauseHandler { get; private set; } = new();

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
}