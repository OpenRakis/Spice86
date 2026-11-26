namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Captures the current state for a single PIT channel without mutating the live timer.
/// </summary>
/// <param name="Count">
///     Current countdown value. A value of zero reflects the terminal count and mirrors the reload semantics that treat
///     zero as the maximum divisor.
/// </param>
/// <param name="Delay">
///     Cached period in milliseconds derived from the loaded divisor; reused while the channel remains in the same mode.
/// </param>
/// <param name="Start">
///     Scheduler index marking when the current cycle began, expressed in milliseconds since the timer was constructed.
/// </param>
/// <param name="ReadLatch">
///     Latched counter value returned by the next data port read when a latch operation has been requested.
/// </param>
/// <param name="WriteLatch">
///     Staged reload value assembled from the write path before committing it to the live counter.
/// </param>
/// <param name="Mode">
///     Active operating mode, matching the control word that programmed the channel.
/// </param>
/// <param name="Bcd">
///     Indicates whether the channel interprets counts using packed BCD instead of binary.
/// </param>
/// <param name="Counting">
///     Reflects whether the channel is actively decrementing (true) or idle because the gate is closed.
/// </param>
/// <param name="ModeChanged">
///     Tracks whether a new control word has been written since the last reload and the mode transition is pending.
/// </param>
/// <param name="GoReadLatch">
///     Signals that the next read should invoke the latch path to refresh <paramref name="ReadLatch" />.
/// </param>
public readonly record struct PitChannelSnapshot(
    int Count,
    double Delay,
    double Start,
    ushort ReadLatch,
    ushort WriteLatch,
    PitMode Mode,
    bool Bcd,
    bool Counting,
    bool ModeChanged,
    bool GoReadLatch);