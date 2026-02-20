namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Emulated clock that derives time from CPU cycles, matching DOSBox staging's
/// <c>PIC_FullIndex</c> timing model.
/// </summary>
/// <remarks>
/// Two time accessors are provided for thread-safe use:
/// <list type="bullet">
///   <item><see cref="ElapsedTimeMs"/> — precise, reads <c>State.Cycles</c> directly
///     (equivalent to DOSBox staging's <c>PIC_FullIndex()</c>).
///     Used from the emulation thread.</item>
///   <item><see cref="AtomicElapsedTimeMs"/> — thread-safe snapshot updated periodically
///     via <see cref="UpdateAtomicIndex"/>. Equivalent to DOSBox staging's
///     <c>PIC_AtomicIndex()</c>. Used from the mixer thread
///     (e.g. <c>AudioCallback</c>).</item>
/// </list>
/// </remarks>
public class EmulatedClock : IEmulatedClock {
    private readonly State _cpuState;
    private long _cyclesPerSecond;

    // Stores the atomic snapshot as raw bits via BitConverter,
    // because C# volatile does not support double directly.
    private long _atomicIndexBits;

    /// <summary>
    /// Creates a cycle-based clock (matching DOSBox staging's PIC_FullIndex).
    /// </summary>
    /// <param name="cpuState">CPU state providing the cycle counter.</param>
    /// <param name="cyclesPerSecond">Number of CPU cycles per second.</param>
    public EmulatedClock(State cpuState, long cyclesPerSecond) {
        _cpuState = cpuState;
        _cyclesPerSecond = cyclesPerSecond;
        StartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets or sets the number of CPU cycles per second.
    /// </summary>
    public long CyclesPerSecond {
        get => _cyclesPerSecond;
        set => _cyclesPerSecond = value;
    }

    public double ElapsedTimeMs => (double)_cpuState.Cycles * 1000 / _cyclesPerSecond;

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

    /// <summary>
    /// Pause is a no-op for cycle-based clocks: cycles don't advance when the CPU is paused.
    /// </summary>
    public void OnPause() {
    }

    /// <summary>
    /// Resume is a no-op for cycle-based clocks: cycles resume advancing with the CPU.
    /// </summary>
    public void OnResume() {
    }
}