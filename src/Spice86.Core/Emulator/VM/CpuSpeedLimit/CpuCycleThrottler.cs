namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;

/// <summary>
/// Throttles CPU execution to maintain a target number of cycles per millisecond.
/// </summary>
public class CpuCycleThrottler : CycleLimiterBase {
    private readonly Stopwatch _performanceStopwatch = new();
    private readonly Stopwatch _highPrecisionSleepStopwatch = new();
    private long _surplusCycles = 0;
    private long _cyclesAtLastCheck;
    private long _lastCheckTime;

    private const int CyclesUp = 1000;
    private const int CyclesDown = 1000;
    private const int MaxCyclesPerMs = 60000;
    private const int MinCyclesPerMs = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuCycleThrottler"/> class.
    /// </summary>
    /// <param name="targetCpuCyclesPerMs">The target CPU cycles per millisecond.</param>
    public CpuCycleThrottler(int targetCpuCyclesPerMs) {
        TargetCpuCyclesPerMs = targetCpuCyclesPerMs > 0
            ? targetCpuCyclesPerMs
            : ICyclesLimiter.RealModeCpuCylcesPerMs;

        _performanceStopwatch.Start();
        _cyclesAtLastCheck = 0;
        _lastCheckTime = _performanceStopwatch.ElapsedMilliseconds;
    }

    /// <inheritdoc/>
    internal override void RegulateCycles(State cpuState, bool isRunning) {
        long currentTime = _performanceStopwatch.ElapsedMilliseconds;
        long elapsedTime = currentTime - _lastCheckTime;

        if (elapsedTime <= 0) {
            _lastCheckTime = currentTime;
            return;
        }

        long currentCycles = cpuState.Cycles;
        long cyclesExecuted = currentCycles - _cyclesAtLastCheck;
        long targetCyclesForPeriod = TargetCpuCyclesPerMs * elapsedTime;
        long cyclesDifference = cyclesExecuted - targetCyclesForPeriod;

        if (cyclesDifference < 0) {
            // We're behind, accumulate surplus cycles
            _surplusCycles += Math.Abs(cyclesDifference);
        } else {
            // We're ahead, reduce surplus cycles first
            if (_surplusCycles > 0) {
                long toConsume = Math.Min(_surplusCycles, cyclesDifference);
                _surplusCycles -= toConsume;
                cyclesDifference -= toConsume;
            }
            // Only sleep if still ahead after surplus is consumed
            DoHighPrecisionSleep(cyclesDifference / TargetCpuCyclesPerMs,
                isRunning);
        }

        _cyclesAtLastCheck = currentCycles;
        _lastCheckTime = _performanceStopwatch.ElapsedMilliseconds;
    }

    private void DoHighPrecisionSleep(double msToSleep, bool isRunning) {
        if (msToSleep <= 0) {
            return;
        }
        _highPrecisionSleepStopwatch.Restart();
        while (isRunning && _highPrecisionSleepStopwatch.ElapsedMilliseconds
            < msToSleep) {
            Thread.SpinWait(1);
        }
    }

    /// <inheritdoc/>
    public override void IncreaseCycles() {
        TargetCpuCyclesPerMs = Math.Min(TargetCpuCyclesPerMs + CyclesUp,
            MaxCyclesPerMs);
    }

    /// <inheritdoc/>
    public override void DecreaseCycles() {
        TargetCpuCyclesPerMs = Math.Max(TargetCpuCyclesPerMs - CyclesDown,
            MinCyclesPerMs);
    }
}