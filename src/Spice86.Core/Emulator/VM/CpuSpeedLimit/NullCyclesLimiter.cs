namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

internal class NullCyclesLimiter : ICyclesLimiter {
    public int TargetCpuCyclesPerMs { get; set; }

    /// <summary>
    /// Always true for unthrottled mode: input is processed every instruction
    /// since there are no tick boundaries.
    /// </summary>
    public bool TickOccurred => true;

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

    public uint TickCount => 0;

    public void OnPause() {
        // No-op
    }

    public void OnResume() {
        // No-op
    }

    public void ConsumeIoCycles(int cycles) {
        // No-op
    }

    public double AtomicFullIndex => 0.0;

    public long NextTickBoundaryCycles => 0;

    public int TickCycleMax => 0;
}
