namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;

/// <summary>
/// Throttles CPU execution to maintain a target number of cycles per millisecond,
/// using a budget-based approach similar to DOSBox.
/// </summary>
public class CpuCycleLimiter : ICyclesLimiter {
    private readonly State _state;
    private readonly SpinWait _spinner = new();
    private readonly Stopwatch _stopwatch = new();
    private long _lastTicks;
    private long _targetCyclesForPause;

    private static readonly long TicksPerMs = Stopwatch.Frequency / 1000;

    private const int CyclesUp = 1000;
    private const int CyclesDown = 1000;
    private const int MaxCyclesPerMs = 60000;
    private const int MinCyclesPerMs = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuCycleLimiter"/> class.
    /// </summary>
    /// <param name="state">The CPU state, used for Cycles and IsRunning properties.</param>
    /// <param name="targetCpuCyclesPerMs">The target CPU cycles per millisecond. A value of 0 is corrected to 3000.</param>
    public CpuCycleLimiter(State state, int targetCpuCyclesPerMs) {
        _state = state;
        if (targetCpuCyclesPerMs > 0) {
            TargetCpuCyclesPerMs = Math.Clamp(
            targetCpuCyclesPerMs,
            MinCyclesPerMs,
            MaxCyclesPerMs);
        } else {
            TargetCpuCyclesPerMs = Math.Clamp(
            ICyclesLimiter.RealModeCpuCyclesPerMs,
            MinCyclesPerMs,
            MaxCyclesPerMs);
        }

        _stopwatch.Start();
    }

    /// <inheritdoc/>
    public void RegulateCycles() {
        if (!_stopwatch.IsRunning) {
            return;
        }

        // If current cycles haven't reached target yet, no need to regulate
        if (_state.Cycles < _targetCyclesForPause) {
            return;
        }

        // We've reached our target, time to regulate speed
        long targetTicks = _lastTicks + TicksPerMs;

        while (_state.IsRunning && _stopwatch.ElapsedTicks < targetTicks) {
            _spinner.SpinOnce();
        }

        _lastTicks = _stopwatch.ElapsedTicks;

        _targetCyclesForPause = _state.Cycles + TargetCpuCyclesPerMs;

        _spinner.Reset();
    }

    /// <inheritdoc />
    public int TargetCpuCyclesPerMs { get; set; }

    /// <inheritdoc/>
    public void IncreaseCycles() {
        TargetCpuCyclesPerMs = Math.Min(TargetCpuCyclesPerMs + CyclesUp,
            MaxCyclesPerMs);
    }

    /// <inheritdoc/>
    public void DecreaseCycles() {
        TargetCpuCyclesPerMs = Math.Max(TargetCpuCyclesPerMs - CyclesDown,
            MinCyclesPerMs);
    }

    /// <inheritdoc/>
    public long GetNumberOfCyclesNotDoneYet() {
        long cyclesRemaining = TargetCpuCyclesPerMs - (_targetCyclesForPause - _state.Cycles);
        return cyclesRemaining;
    }

    /// <inheritdoc/>
    public double GetCycleProgressionPercentage() {
        if (TargetCpuCyclesPerMs == 0) {
            return 0.0;
        }
        return GetNumberOfCyclesNotDoneYet() / TargetCpuCyclesPerMs;
    }
}