namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;
using System.Threading;

/// <summary>
/// Throttles CPU execution to maintain a target number of cycles per millisecond,
/// using a budget-based approach matching DOSBox staging's increase_ticks() + TIMER_AddTick().
/// </summary>
public class CpuCycleLimiter : ICyclesLimiter {
    private readonly State _state;
    private readonly Stopwatch _stopwatch = new();
    private long _lastTicks;
    private long _targetCyclesForPause;
    private uint _tickCount;
    private double _atomicFullIndex;

    private static readonly long TicksPerMs = Stopwatch.Frequency / 1000;

    private const int CyclesUp = 1000;
    private const int CyclesDown = 1000;
    private const int MaxCyclesPerMs = 60000;
    private const int MinCyclesPerMs = 100;

    /// <summary>
    /// Maximum ticks the emulator may run without throttling when behind wall-clock.
    /// Reference: DOSBox dosbox.cpp caps ticks.remain at 20.
    /// </summary>
    private const int MaxCatchUpTicks = 20;

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

        // First tick boundary: CPU must execute TargetCpuCyclesPerMs cycles before the first tick completes.
        // Reference: DOSBox TIMER_AddTick() sets CPU_CycleLeft=CPU_CycleMax at the start of each tick.
        _targetCyclesForPause = TargetCpuCyclesPerMs;

        _stopwatch.Start();
        _lastTicks = _stopwatch.ElapsedTicks;
    }

    /// <inheritdoc/>
    public void RegulateCycles() {
        // Fast path: if current cycles haven't reached the tick boundary, nothing to do.
        // This is the hot path — called every instruction — so it must be as cheap as possible.
        // Reference: DOSBox normal_loop() only enters PIC_RunQueue/increase_ticks when CPU_Cycles are exhausted.
        if (_state.Cycles < _targetCyclesForPause) {
            return;
        }

        if (!_stopwatch.IsRunning) {
            return;
        }

        // A full tick's worth of cycles has been consumed.
        // Reference: DOSBox TIMER_AddTick() increments PIC_Ticks.
        _tickCount++;

        // Set next tick boundary immediately (like DOSBox's CPU_CycleLeft = CPU_CycleMax).
        _targetCyclesForPause = _state.Cycles + TargetCpuCyclesPerMs;

        // Throttle: determine whether we're ahead of or behind wall-clock.
        // Reference: DOSBox increase_ticks(): measures wall-clock, sleeps if ahead,
        // runs at full speed if behind, caps catch-up at 20 ticks.
        long targetTicks = _lastTicks + TicksPerMs;
        long now = _stopwatch.ElapsedTicks;

        if (now < targetTicks) {
            // Ahead of real-time. Busy-spin until wall-clock catches up.
            // CRITICAL: Do NOT use SpinWait.SpinOnce() — it escalates to
            // Thread.Sleep(1) after ~35 iterations, which on Windows sleeps
            // for ~15.6ms (default timer resolution). That would make each
            // 1ms tick take 15ms, slowing emulation to 1/15th speed.
            // Thread.SpinWait(1) issues a single PAUSE instruction without
            // any escalation, keeping sub-microsecond precision.
            while (_state.IsRunning && _stopwatch.ElapsedTicks < targetTicks) {
                Thread.SpinWait(1);
            }
        }

        // Track where we SHOULD be, not where we ARE.
        // This prevents cumulative drift from wait overshoot.
        // Reference: DOSBox ticks.last = ticks_new tracks the expected position.
        _lastTicks = targetTicks;

        // If we've fallen too far behind (> MaxCatchUpTicks ms), reset baseline
        // to prevent a burst of unthrottled ticks that would starve audio/UI.
        // Reference: DOSBox caps ticks.remain at 20.
        now = _stopwatch.ElapsedTicks;
        long maxBehind = TicksPerMs * MaxCatchUpTicks;
        if (now > _lastTicks + maxBehind) {
            _lastTicks = now - maxBehind;
        }

        // Update atomic index again after the tick boundary advanced.
        UpdateAtomicIndex();
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
    public uint TickCount => _tickCount;

    /// <inheritdoc/>
    public long GetNumberOfCyclesNotDoneYet() {
        // Cycles consumed within the current tick.
        // Reference: DOSBox PIC_TickIndexND() = CPU_CycleMax - CPU_CycleLeft - CPU_Cycles
        long tickStart = _targetCyclesForPause - TargetCpuCyclesPerMs;
        return _state.Cycles - tickStart;
    }

    /// <inheritdoc/>
    public double GetCycleProgressionPercentage() {
        if (TargetCpuCyclesPerMs == 0) {
            return 0.0;
        }
        // Reference: DOSBox PIC_TickIndex() = PIC_TickIndexND() / CPU_CycleMax
        long tickStart = _targetCyclesForPause - TargetCpuCyclesPerMs;
        long cyclesDone = _state.Cycles - tickStart;
        double fraction = (double)cyclesDone / TargetCpuCyclesPerMs;
        return Math.Clamp(fraction, 0.0, 1.0);
    }

    /// <inheritdoc/>
    public void OnPause() {
        _stopwatch.Stop();
    }

    /// <inheritdoc/>
    public void OnResume() {
        _stopwatch.Start();
        // Reset the throttle baseline to prevent a burst of immediate ticks after resume.
        _lastTicks = _stopwatch.ElapsedTicks;
    }

    /// <inheritdoc/>
    public void ConsumeIoCycles(int cycles) {
        // Lower the tick boundary so the remaining budget for this tick is reduced.
        // Equivalent to DOSBox: CPU_Cycles -= delaycyc
        // This means RegulateCycles() will trigger the next tick sooner.
        long remaining = _targetCyclesForPause - _state.Cycles;
        int clamped = (int)Math.Min(cycles, remaining);
        if (clamped > 0) {
            _targetCyclesForPause -= clamped;
        }
    }

    /// <inheritdoc/>
    public double AtomicFullIndex => Volatile.Read(ref _atomicFullIndex);

    /// <summary>
    /// Updates the atomic snapshot of FullIndex for cross-thread readers.
    /// Reference: DOSBox PIC_UpdateAtomicIndex() stores PIC_FullIndex() atomically.
    /// </summary>
    private void UpdateAtomicIndex() {
        double fullIndex = _tickCount + GetCycleProgressionPercentage();
        Volatile.Write(ref _atomicFullIndex, fullIndex);
    }
}