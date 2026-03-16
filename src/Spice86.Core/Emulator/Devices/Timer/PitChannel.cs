namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Holds the mutable state for an individual PIT channel, including the staged latches and control flags.
/// </summary>
/// <remarks>
///     <para>
///         The countdown fields mirror the reference implementation: <see cref="Count" /> stores the active divisor,
///         <see cref="Delay" /> caches the millisecond period, and <see cref="Start" /> records the scheduler index at
///         which
///         the current cycle began.
///     </para>
///     <para>
///         The read and write latches assemble control-port data according to the access mode machine. The boolean
///         flags model the reference flip-flops: <see cref="GoReadLatch" /> toggles whether a refreshed latch is required,
///         <see cref="ModeChanged" /> stays true until a reload occurs, <see cref="CounterStatusSet" /> mirrors the
///         latched-status path, <see cref="Counting" /> tracks gate-controlled activity, and <see cref="UpdateCount" />
///         signals
///         deferred reloads in mode 2.
///     </para>
/// </remarks>
internal struct PitChannel {
    /// <summary>
    ///     Current divisor value. A zero entry mirrors the hardware behavior that treats it as 65536 (or 10000 in BCD).
    /// </summary>
    public int Count;

    /// <summary>
    ///     Cached delay in milliseconds computed from the divisor. Used to schedule the next event without recomputing.
    /// </summary>
    public double Delay;

    /// <summary>
    ///     Scheduler index (milliseconds) marking when the current countdown started.
    /// </summary>
    public double Start;

    /// <summary>
    ///     Latched counter value returned to the CPU on the next data-port read.
    /// </summary>
    public ushort ReadLatch;

    /// <summary>
    ///     Accumulates incoming bytes from the write path before committing them to <see cref="Count" />.
    /// </summary>
    public ushort WriteLatch;

    /// <summary>
    ///     Active operating mode for the channel.
    /// </summary>
    public PitMode Mode;

    /// <summary>
    ///     Access mode used when servicing reads.
    /// </summary>
    public AccessMode ReadMode;

    /// <summary>
    ///     Access mode used when servicing writes.
    /// </summary>
    public AccessMode WriteMode;

    /// <summary>
    ///     Indicates whether the channel interprets counts as BCD values.
    /// </summary>
    public bool Bcd;

    /// <summary>
    ///     Indicates whether the next read should refresh the latch before returning data.
    /// </summary>
    public bool GoReadLatch;

    /// <summary>
    ///     Tracks whether a new control word has been written without reloading the counter yet.
    /// </summary>
    public bool ModeChanged;

    /// <summary>
    ///     When true, the status byte has been latched and must be returned before a new status is captured.
    /// </summary>
    public bool CounterStatusSet;

    /// <summary>
    ///     Reflects whether the counter is actively decrementing, subject to the gate input.
    /// </summary>
    public bool Counting;

    /// <summary>
    ///     Signals that the divisor update should be applied when the current period completes (mode 2 behavior).
    /// </summary>
    public bool UpdateCount;
}
