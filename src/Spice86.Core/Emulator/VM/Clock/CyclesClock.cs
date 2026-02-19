namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// A cycle-based clock that derives time from CPU cycle count, matching
/// DOSBox staging's PIC_FullIndex / PIC_AtomicIndex pattern.
/// </summary>
/// <remarks>
/// Two time accessors are provided:
/// <list type="bullet">
///   <item><see cref="ElapsedTimeMs"/> — precise, reads State.Cycles directly.
///     Equivalent to DOSBox staging's <c>PIC_FullIndex()</c>.
///     Used from the emulation thread (e.g. <c>RenderUpToNow</c>).</item>
///   <item><see cref="AtomicElapsedTimeMs"/> — thread-safe snapshot updated
///     periodically via <see cref="UpdateAtomicIndex"/>.
///     Equivalent to DOSBox staging's <c>PIC_AtomicIndex()</c>.
///     Used from the mixer thread (e.g. <c>AudioCallback</c>).</item>
/// </list>
/// </remarks>
public class CyclesClock : IEmulatedClock {
    private readonly State _cpuState;
    // Stores the atomic snapshot as raw bits via BitConverter,
    // because C# volatile does not support double directly.
    private long _atomicIndexBits;

    public CyclesClock(State cpuState, long cyclesPerSecond, DateTime? startTime = null) {
        _cpuState = cpuState;
        CyclesPerSecond = cyclesPerSecond;
        StartTime = startTime ?? DateTime.UtcNow;
    }

    public long CyclesPerSecond { get; set; }

    /// <summary>
    /// Returns the precise elapsed time based on the current cycle count.
    /// Equivalent to DOSBox staging's <c>PIC_FullIndex()</c>.
    /// Must only be called from the emulation thread.
    /// </summary>
    public double ElapsedTimeMs => (double)_cpuState.Cycles * 1000 / CyclesPerSecond;

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
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }

    public void OnResume() {
        // No-op: when CPU is paused, cycles don't advance, so time naturally stops
    }
}