namespace Spice86.Core.Emulator.VM.CycleBudget;

public class StaticCyclesBudgeter(int cycleBudget) : ICyclesBudgeter {
    public int GetNextSliceBudget() {
        return cycleBudget;
    }

    public void UpdateBudget(double elapsedMilliseconds, long cyclesExecuted, bool cpuStateIsRunning) {
        //nop
    }
}