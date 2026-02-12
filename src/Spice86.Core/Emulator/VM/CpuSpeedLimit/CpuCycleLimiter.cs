namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Utils;

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

    /// <summary>
    /// The cycle budget that was active when the current tick started.
    /// Snapshotted at each tick boundary so that FullIndex (which uses this as
    /// denominator) is continuous and monotonic — exactly like DOSBox's CPU_CycleMax
    /// which does not change within a tick.
    /// Reference: DOSBox src/cpu/cpu.h CPU_CycleMax
    /// </summary>
    private int _tickCycleMax;

    /// <summary>
    /// Accumulated IO delay cycles removed during the current tick.
    /// Reset to 0 at each tick boundary, matching DOSBox's CPU_IODelayRemoved.
    /// Reference: DOSBox src/cpu/cpu.cpp CPU_IODelayRemoved, reset in dosbox.cpp
    /// </summary>
    private long _ioDelayRemoved;

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
        _tickCycleMax = TargetCpuCyclesPerMs;
        _targetCyclesForPause = _tickCycleMax;

        _stopwatch.Start();
        _lastTicks = _stopwatch.ElapsedTicks;
    }

    /// <inheritdoc/>
    public bool TickOccurred { get; private set; }

    /// <inheritdoc/>
    public void RegulateCycles() {
        // Fast path: if current cycles haven't reached the tick boundary, nothing to do.
        // This is the hot path — called every instruction — so it must be as cheap as possible.
        // Reference: DOSBox normal_loop() only enters PIC_RunQueue/increase_ticks when CPU_Cycles are exhausted.
        TickOccurred = false;
        if (_state.Cycles < _targetCyclesForPause) {
            return;
        }

        if (!_stopwatch.IsRunning) {
            return;
        }

        // A full tick's worth of cycles has been consumed.
        // Reference: DOSBox TIMER_AddTick() increments PIC_Ticks.
        TickOccurred = true;
        _tickCount++;

        // Reset IO delay tracking for the new tick.
        // Reference: DOSBox dosbox.cpp: CPU_IODelayRemoved = 0; at tick boundary.
        _ioDelayRemoved = 0;

        // Snapshot the current target for the new tick, like DOSBox's
        // TIMER_AddTick() which uses CPU_CycleMax for the entire tick.
        // TargetCpuCyclesPerMs may have changed via IncreaseCycles/DecreaseCycles
        // during the previous tick, but we only apply it at the tick boundary.
        _tickCycleMax = TargetCpuCyclesPerMs;

        // Set next tick boundary immediately (like DOSBox's CPU_CycleLeft = CPU_CycleMax).
        _targetCyclesForPause = _state.Cycles + _tickCycleMax;

        // Throttle: determine whether we're ahead of or behind wall-clock.
        // Reference: DOSBox increase_ticks(): measures wall-clock, sleeps if ahead,
        // runs at full speed if behind, caps catch-up at 20 ticks.
        long targetTicks = _lastTicks + TicksPerMs;
        long now = _stopwatch.ElapsedTicks;

        if (now < targetTicks) {
            // Ahead of real-time. Wait using graduated strategy:
            // >= 1ms: ManualResetEventSlim (no CPU burn)
            // 0.05–1ms: SpinWait + Yield
            // < 0.05ms: pure spin
            HighResolutionWaiter.WaitUntil(_stopwatch, targetTicks);
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

    /// <inheritdoc />
    public long NextTickBoundaryCycles => _targetCyclesForPause;

    /// <inheritdoc />
    public int TickCycleMax => _tickCycleMax;

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
        long tickStart = _targetCyclesForPause - _tickCycleMax;
        return _state.Cycles - tickStart;
    }

    /// <inheritdoc/>
    public double GetCycleProgressionPercentage() {
        if (_tickCycleMax == 0) {
            return 0.0;
        }
        // Reference: DOSBox PIC_TickIndex() = PIC_TickIndexND() / CPU_CycleMax
        // Uses _tickCycleMax (snapshotted at tick boundary) as denominator,
        // ensuring FullIndex is continuous and monotonic within a tick.
        // DOSBox does NOT clamp PIC_TickIndex() — it can momentarily exceed 1.0
        // when an instruction overshoots the tick boundary. Clamping would create
        // a timing plateau followed by a discontinuity, causing audio artifacts.
        long tickStart = _targetCyclesForPause - _tickCycleMax;
        long cyclesDone = _state.Cycles - tickStart;
        double fraction = (double)cyclesDone / _tickCycleMax;
        return Math.Max(fraction, 0.0);
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
    public long IoDelayRemoved => _ioDelayRemoved;

    /// <inheritdoc/>
    public void ConsumeIoCycles(int cycles) {
        // Advance the emulated clock by adjusting the next tick boundary.
        // This keeps the timing accounting consistent within the current tick
        // without requiring State.Cycles to be modified.
        // Reference: DOSBox src/hardware/port.cpp IO_USEC_read_delay(), IO_USEC_write_delay()
        long remaining = _targetCyclesForPause - _state.Cycles;
        int clamped = (int)Math.Min(cycles, remaining);
        if (clamped > 0) {
            // Advance the next tick boundary to account for IO delay cycles.
            // This effectively advances the emulated clock forward without needing
            // to modify the Cycles property in State.
            _targetCyclesForPause += clamped;
            _ioDelayRemoved += clamped;
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