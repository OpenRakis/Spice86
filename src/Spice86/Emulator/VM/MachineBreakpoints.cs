namespace Spice86.Emulator.VM;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM.Breakpoint;
using Spice86.Emulator.Memory;

public class MachineBreakpoints {
    private readonly BreakPointHolder cycleBreakPoints = new();

    private readonly BreakPointHolder executionBreakPoints = new();

    private readonly Memory memory;

    private readonly PauseHandler pauseHandler = new();

    private readonly State state;

    private BreakPoint? machineStopBreakPoint;

    public MachineBreakpoints(Machine machine) {
        this.state = machine.GetCpu().GetState();
        this.memory = machine.GetMemory();
    }

    public void CheckBreakPoint() {
        CheckBreakPoints();
        pauseHandler.WaitIfPaused();
    }

    public PauseHandler GetPauseHandler() {
        return pauseHandler;
    }

    public void OnMachineStop() {
        if (machineStopBreakPoint is not null) {
            machineStopBreakPoint.Trigger();
            pauseHandler.WaitIfPaused();
        }
    }

    public void ToggleBreakPoint(BreakPoint? breakPoint, bool on) {
        if (breakPoint is null) {
            return;
        }
        BreakPointType? breakPointType = breakPoint.GetBreakPointType();
        if (breakPointType == BreakPointType.EXECUTION) {
            executionBreakPoints.ToggleBreakPoint(breakPoint, on);
        } else if (breakPointType == BreakPointType.CYCLES) {
            cycleBreakPoints.ToggleBreakPoint(breakPoint, on);
        } else if (breakPointType == BreakPointType.MACHINE_STOP) {
            machineStopBreakPoint = breakPoint;
        } else {
            memory.ToggleBreakPoint(breakPoint, on);
        }
    }

    private void CheckBreakPoints() {
        if (!executionBreakPoints.IsEmpty()) {
            uint address = state.GetIpPhysicalAddress();
            executionBreakPoints.TriggerMatchingBreakPoints(address);
        }

        if (!cycleBreakPoints.IsEmpty()) {
            long cycles = state.GetCycles();
            cycleBreakPoints.TriggerMatchingBreakPoints(cycles);
        }
    }
}