namespace Ix86.Emulator.Machine;

using Ix86.Emulator.Cpu;
using Ix86.Emulator.Machine.Breakpoint;
using Ix86.Emulator.Memory;

public class MachineBreakpoints
{
    private readonly State state;
    private readonly Memory memory;
    private readonly PauseHandler pauseHandler = new();
    private BreakPoint? machineStopBreakPoint;
    private readonly BreakPointHolder executionBreakPoints = new();
    private readonly BreakPointHolder cycleBreakPoints = new();
    public MachineBreakpoints(Machine machine)
    {
        this.state = machine.GetCpu().GetState();
        this.memory = machine.GetMemory();
    }

    public virtual void ToggleBreakPoint(BreakPoint breakPoint, bool on)
    {
        BreakPointType breakPointType = breakPoint.GetBreakPointType();
        if (breakPointType.Equals(BreakPointType.EXECUTION))
        {
            executionBreakPoints.ToggleBreakPoint(breakPoint, on);
        }
        else if (breakPointType.Equals(BreakPointType.CYCLES))
        {
            cycleBreakPoints.ToggleBreakPoint(breakPoint, on);
        }
        else if (breakPointType.Equals(BreakPointType.MACHINE_STOP))
        {
            machineStopBreakPoint = breakPoint;
        }
        else
        {
            memory.ToggleBreakPoint(breakPoint, on);
        }
    }

    private void CheckBreakPoints()
    {
        if (!executionBreakPoints.IsEmpty())
        {
            int address = state.GetIpPhysicalAddress();
            executionBreakPoints.TriggerMatchingBreakPoints(address);
        }

        if (!cycleBreakPoints.IsEmpty())
        {
            long cycles = state.GetCycles();
            cycleBreakPoints.TriggerMatchingBreakPoints(cycles);
        }
    }

    public virtual void CheckBreakPoint()
    {
        CheckBreakPoints();
        pauseHandler.WaitIfPaused();
    }

    public virtual void OnMachineStop()
    {
        if (machineStopBreakPoint is not null)
        {
            machineStopBreakPoint.Trigger();
            pauseHandler.WaitIfPaused();
        }
    }

    public virtual PauseHandler GetPauseHandler()
    {
        return pauseHandler;
    }
}
