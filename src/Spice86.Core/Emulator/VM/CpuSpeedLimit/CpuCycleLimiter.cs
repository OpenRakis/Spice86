namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;
using static Spice86.Core.Emulator.Devices.Timer.Timer;

using System.Diagnostics;

/// <summary>
/// Throttles CPU execution to maintain a target number of cycles per millisecond,
/// using a budget-based approach similar to DOSBox.
/// </summary>
public class CpuCycleLimiter : CycleLimiterBase {
    // Keep track of timing and cycles
    private readonly SpinWait _spinner = new();
    private readonly Stopwatch _stopwatch = new();
    private long _cycleRemain;
    private long _lastTicks;

    // Constants for cycle control
    private const int MaxCyclesPerWindow = 20;
    private const int CyclesUp = 1000;
    private const int CyclesDown = 1000;
    private const int MaxCyclesPerMs = 60000;
    private const int MinCyclesPerMs = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuCycleLimiter"/> class.
    /// </summary>
    /// <param name="targetCpuCyclesPerMs">The target CPU cycles per millisecond. A value of 0 is corrected to 3000.</param>
    public CpuCycleLimiter(int targetCpuCyclesPerMs) {
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
        _lastTicks = _stopwatch.ElapsedTicks;
    }

    /// <inheritdoc/>
    internal override void RegulateCycles(State cpuState) {
        if (!cpuState.IsRunning) {
            return;
        }

        // If we still have cycle budget left, decrement and return
        if (_cycleRemain > 0) {
            _cycleRemain--;
            return;
        }

        // We've used all allocated cycles, need to calculate a new budget
        long currentTicks = _stopwatch.ElapsedTicks;

        // If time hasn't advanced significantly, make the emulation wait
        if (currentTicks - _lastTicks < StopwatchTicksPerMillisecond) {
            // Less than 0.1ms has passed
            // Wait until at least 1ms has passed,
            // using the same fast approach as Renderer.cs
            long targetTicks = _lastTicks + StopwatchTicksPerMillisecond;

            while (cpuState.IsRunning && _stopwatch.ElapsedTicks < targetTicks) {
                _spinner.SpinOnce();
            }

            currentTicks = _stopwatch.ElapsedTicks;
        }

        // Calculate elapsed milliseconds
        // (floating point for sub-millisecond precision)
        double elapsedMs = (double)(currentTicks - _lastTicks) / Stopwatch.Frequency * 1000;

        _lastTicks = currentTicks;

        // Calculate cycle budget with floating point to avoid losing
        // sub-millisecond precision
        _cycleRemain = (long)Math.Min(
            TargetCpuCyclesPerMs * elapsedMs,
            TargetCpuCyclesPerMs * MaxCyclesPerWindow);

        _spinner.Reset();
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