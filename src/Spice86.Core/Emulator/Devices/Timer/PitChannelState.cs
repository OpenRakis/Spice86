namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Complete state of one PIT channel, as captured by <see cref="PitTimer.CaptureState" />.
/// </summary>
/// <remarks>
///     <see cref="PitChannelSnapshot" /> is a view for inspection and leaves out the access modes and two flags.
///     This record holds every field of the channel, so a channel can be restored from it.
/// </remarks>
/// <param name="Count">Divisor in use. Zero stands for 65536, or 10000 in BCD.</param>
/// <param name="Delay">Period in milliseconds computed from the divisor.</param>
/// <param name="Start">
///     Time the current period began, in milliseconds on the emulated clock at capture. On restore it is moved by
///     the time that passed on that clock since <see cref="PitTimerState.CapturedAtMs" />.
/// </param>
/// <param name="ReadLatch">Latched counter value the next data port read returns.</param>
/// <param name="WriteLatch">Reload value being assembled from data port writes.</param>
/// <param name="Mode">Operating mode.</param>
/// <param name="ReadMode">Position of the read access state machine.</param>
/// <param name="WriteMode">Position of the write access state machine.</param>
/// <param name="Bcd">Whether the channel counts in BCD.</param>
/// <param name="GoReadLatch">Whether the next read refreshes <paramref name="ReadLatch" /> first.</param>
/// <param name="ModeChanged">Whether a control word was written and no reload followed yet.</param>
/// <param name="CounterStatusSet">Whether a latched status byte waits to be read.</param>
/// <param name="Counting">Whether the counter runs, which depends on the gate.</param>
/// <param name="UpdateCount">Whether a new divisor applies when the current period ends.</param>
public readonly record struct PitChannelState(
    int Count,
    double Delay,
    double Start,
    ushort ReadLatch,
    ushort WriteLatch,
    PitMode Mode,
    AccessMode ReadMode,
    AccessMode WriteMode,
    bool Bcd,
    bool GoReadLatch,
    bool ModeChanged,
    bool CounterStatusSet,
    bool Counting,
    bool UpdateCount);
