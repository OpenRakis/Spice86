namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

internal class NullCyclesLimiter : ICyclesLimiter {
    public int TargetCpuCyclesPerMs { get; set; }

    public void RegulateCycles() {
        //NOP
    }

    public void DecreaseCycles() {
        //NOP
    }

    public void IncreaseCycles() {
        //NOP
    }

    public long GetNumberOfCyclesNotDoneYet() {
        return 1;
    }

    public double GetCycleProgressionPercentage() {
        return 0.0;
    }
}
