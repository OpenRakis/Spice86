namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
///     A scheduler event the Sound Blaster had queued at capture.
/// </summary>
/// <param name="Event">Kind of event.</param>
/// <param name="DelayMs">Milliseconds from capture to the event. Negative when the event was overdue.</param>
/// <param name="Value">Value the event's handler receives, a number of bytes for the DMA events.</param>
public readonly record struct SoundBlasterScheduledEvent(SoundBlasterEvent Event, double DelayMs, uint Value);
