namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;

/// <summary>
/// Emulated clock that derives time from CPU cycles when <see cref="State"/> is provided,
/// matching DOSBox staging's <c>PIC_FullIndex</c> timing model. Falls back to
/// wall-clock (<see cref="Stopwatch"/>) when running without cycle tracking.
/// </summary>
/// <remarks>
/// Two time accessors are provided for thread-safe use:
/// <list type="bullet">
///   <item><see cref="ElapsedTimeMs"/> — precise, reads <c>State.Cycles</c> directly
///     (equivalent to DOSBox staging's <c>PIC_FullIndex()</c>). When no <c>State</c>
///     is provided, reads from <see cref="Stopwatch"/>. Used from the emulation thread.</item>
///   <item><see cref="AtomicElapsedTimeMs"/> — thread-safe snapshot updated periodically
///     via <see cref="UpdateAtomicIndex"/>. Equivalent to DOSBox staging's
///     <c>PIC_AtomicIndex()</c>. Used from the mixer thread
///     (e.g. <c>AudioCallback</c>).</item>
/// </list>
/// </remarks>
public class EmulatedClock : IEmulatedClock {
    private readonly State? _cpuState;
    private long _cyclesPerSecond;

    // Wall-clock fallback when no CPU state is provided
    private int _ticks;
    private readonly Stopwatch _stopwatch = new();
    private double _cachedTime;

    // Stores the atomic snapshot as raw bits via BitConverter,
    // because C# volatile does not support double directly.
    private long _atomicIndexBits;

    /// <summary>
    /// Creates a cycle-based clock (matching DOSBox staging's PIC_FullIndex).
    /// </summary>
    /// <param name="cpuState">CPU state providing the cycle counter.</param>
    /// <param name="cyclesPerSecond">Number of CPU cycles per second.</param>
    /// <param name="startTime">Optional start time for date/time calculations.</param>
    public EmulatedClock(State cpuState, long cyclesPerSecond, DateTime? startTime = null) {
        _cpuState = cpuState;
        _cyclesPerSecond = cyclesPerSecond;
        StartTime = startTime ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a wall-clock (Stopwatch) based clock. Used when CPU state is not available.
    /// </summary>
    /// <param name="startTime">Optional start time for date/time calculations.</param>
    public EmulatedClock(DateTime? startTime = null) {
        StartTime = startTime ?? DateTime.UtcNow;
        _stopwatch.Start();
    }

    /// <summary>
    /// Gets or sets the number of CPU cycles per second.
    /// Only meaningful when using cycle-based timing.
    /// </summary>
    public long CyclesPerSecond {
        get => _cyclesPerSecond;
        set => _cyclesPerSecond = value;
    }

    /// <summary>
    /// Gets whether this clock uses cycle-based timing (vs wall-clock).
    /// </summary>
    public bool IsCycleBased => _cpuState is not null;

    public double ElapsedTimeMs {
        get {
            if (_cpuState is not null) {
                return (double)_cpuState.Cycles * 1000 / _cyclesPerSecond;
            }

            // Wall-clock fallback: Stopwatch.GetTimestamp can be slow,
            // so we only query it periodically.
            if (_ticks++ % 100 != 0) {
                return _cachedTime;
            }
            _cachedTime = _stopwatch.Elapsed.TotalMilliseconds;
            return _cachedTime;
        }
    }

    /// <summary>
    /// Returns a thread-safe snapshot of elapsed time, updated periodically
    /// by <see cref="UpdateAtomicIndex"/>. Equivalent to DOSBox staging's
    /// <c>PIC_AtomicIndex()</c>. Safe to call from the mixer thread.
    /// </summary>
    public double AtomicElapsedTimeMs =>
        BitConverter.Int64BitsToDouble(Interlocked.Read(ref _atomicIndexBits));

    /// <summary>
    /// Stores the current <see cref="ElapsedTimeMs"/> into the atomic snapshot.
    /// Equivalent to DOSBox staging's <c>PIC_UpdateAtomicIndex()</c>.
    /// Called from the emulation thread during <c>RenderUpToNow</c>.
    /// </summary>
    public void UpdateAtomicIndex() {
        Interlocked.Exchange(ref _atomicIndexBits,
            BitConverter.DoubleToInt64Bits(ElapsedTimeMs));
    }

    public DateTime StartTime { get; set; }

    public DateTime CurrentDateTime => StartTime.AddMilliseconds(ElapsedTimeMs);

    public void OnPause() {
        if (_cpuState is null) {
            _stopwatch.Stop();
        }
        // When cycle-based, pause is a no-op: cycles don't advance when CPU is paused
    }

    public void OnResume() {
        if (_cpuState is null) {
            _stopwatch.Start();
        }
        // When cycle-based, resume is a no-op: cycles don't advance when CPU is paused
    }
}