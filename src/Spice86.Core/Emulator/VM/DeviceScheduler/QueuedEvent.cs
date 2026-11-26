namespace Spice86.Core.Emulator.VM.DeviceScheduler;

/// <summary>
///     An event waiting in a <see cref="DeviceScheduler" />, as returned by <see cref="DeviceScheduler.GetEvents" />.
/// </summary>
/// <param name="TimeMs">Time on the emulated clock, in milliseconds, at which the event fires.</param>
/// <param name="Value">Value the handler receives.</param>
public readonly record struct QueuedEvent(double TimeMs, uint Value);
