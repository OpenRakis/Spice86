namespace Spice86.Core.Emulator.VM.CycleBudget;

public interface ICyclesBudgeter {
    int GetNextSliceBudget();
    void UpdateBudget(double elapsedMilliseconds, long cyclesExecuted, bool cpuStateIsRunning);
}