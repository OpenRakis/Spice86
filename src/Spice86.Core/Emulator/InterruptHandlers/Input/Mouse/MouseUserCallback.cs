namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.Input.Mouse;

/// <summary>
///     Represents a user-defined callback function for when a mouse event occurs.
/// </summary>
/// <param name="TriggerMask">Specify the mask of which event types to trigger on</param>
/// <param name="Segment">Segment of address to call when matching event happens</param>
/// <param name="Offset">Offset of address to call when matching event happens</param>
public record struct MouseUserCallback(MouseEventMask TriggerMask, ushort Segment, ushort Offset);