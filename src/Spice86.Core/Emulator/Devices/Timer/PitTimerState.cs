namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Complete state of the programmable interval timer, as captured by <see cref="PitTimer.CaptureState" />.
/// </summary>
/// <remarks>
///     The state of the IRQ 0 line is part of the interrupt controller's state, and the state of the PC speaker
///     is the speaker's own. Neither is held here.
/// </remarks>
/// <param name="Channel0">Channel 0, which raises IRQ 0.</param>
/// <param name="Channel1">Channel 1.</param>
/// <param name="Channel2">Channel 2, which drives the PC speaker.</param>
/// <param name="IsChannel2GateHigh">Level of the channel 2 gate input.</param>
/// <param name="LatchedTimerStatus">Status byte latched by a read-back command.</param>
/// <param name="LatchedTimerStatusLocked">Whether the latched status byte waits to be read.</param>
/// <param name="CapturedAtMs">Elapsed milliseconds on the emulated clock at capture.</param>
/// <param name="Channel0EventDelayMs">
///     Milliseconds from capture to the channel 0 event queued in the scheduler, which raises IRQ 0. Negative
///     when the event was overdue. <see langword="null" /> when no event was queued.
/// </param>
public readonly record struct PitTimerState(
    PitChannelState Channel0,
    PitChannelState Channel1,
    PitChannelState Channel2,
    bool IsChannel2GateHigh,
    byte LatchedTimerStatus,
    bool LatchedTimerStatusLocked,
    double CapturedAtMs,
    double? Channel0EventDelayMs);
