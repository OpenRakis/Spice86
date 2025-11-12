namespace Spice86.Core.Emulator.VM.CycleBudget;

using Spice86.Core.Emulator.VM.CpuSpeedLimit;

/// <summary>
///     Dynamically adjusts the per-slice CPU cycle budget so that emulation stays in sync with real time.
/// </summary>
public sealed class AdaptiveCyclesBudgeter : ICyclesBudgeter {
    private const double MinAdaptiveScaleFactor = 0.25;
    private const double MaxAdaptiveScaleFactor = 4.0;

    private readonly ICyclesLimiter _cyclesLimiter;
    private readonly double _sliceDurationMilliseconds;
    private double _adaptiveCyclesPerSlice;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdaptiveCyclesBudgeter" /> class.
    /// </summary>
    /// <param name="cyclesLimiter">Limiter exposing the user-configured cycles-per-millisecond target.</param>
    /// <param name="sliceDurationMilliseconds">Duration, in milliseconds, of each emulation slice.</param>
    public AdaptiveCyclesBudgeter(ICyclesLimiter cyclesLimiter, double sliceDurationMilliseconds) {
        _cyclesLimiter = cyclesLimiter;
        _sliceDurationMilliseconds = Math.Max(0.001, sliceDurationMilliseconds);
        _adaptiveCyclesPerSlice = Math.Max(1, _cyclesLimiter.TargetCpuCyclesPerMs);
    }

    /// <summary>
    ///     Gets the next cycle budget to apply to a slice, respecting user-defined limits.
    /// </summary>
    public int GetNextSliceBudget() {
        int maxCyclesPerMs = Math.Max(1, _cyclesLimiter.TargetCpuCyclesPerMs);
        if (double.IsNaN(_adaptiveCyclesPerSlice) || double.IsInfinity(_adaptiveCyclesPerSlice) ||
            _adaptiveCyclesPerSlice <= 0) {
            _adaptiveCyclesPerSlice = maxCyclesPerMs;
        }

        if (_adaptiveCyclesPerSlice > maxCyclesPerMs) {
            _adaptiveCyclesPerSlice = maxCyclesPerMs;
        }

        int budget = (int)Math.Round(_adaptiveCyclesPerSlice);
        return Math.Clamp(budget, 1, maxCyclesPerMs);
    }

    /// <summary>
    ///     Updates the adaptive budget using the elapsed wall-clock milliseconds and executed cycle count.
    /// </summary>
    /// <param name="elapsedMilliseconds">Duration spent executing the slice.</param>
    /// <param name="cyclesExecuted">Number of CPU cycles retired inside the slice.</param>
    /// <param name="isCpuRunning">Whether the CPU is still running (prevents late updates during shutdown).</param>
    public void UpdateBudget(double elapsedMilliseconds, long cyclesExecuted, bool isCpuRunning) {
        if (!isCpuRunning || elapsedMilliseconds <= 0 || cyclesExecuted <= 0) {
            return;
        }

        double scale = _sliceDurationMilliseconds / elapsedMilliseconds;
        scale = Math.Clamp(scale, MinAdaptiveScaleFactor, MaxAdaptiveScaleFactor);

        int maxCyclesPerMs = Math.Max(1, _cyclesLimiter.TargetCpuCyclesPerMs);
        double adjustedBudget = Math.Clamp(_adaptiveCyclesPerSlice * scale, 1.0, maxCyclesPerMs);
        _adaptiveCyclesPerSlice = adjustedBudget;
    }
}