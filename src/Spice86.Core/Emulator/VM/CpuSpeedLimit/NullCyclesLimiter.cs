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
}
