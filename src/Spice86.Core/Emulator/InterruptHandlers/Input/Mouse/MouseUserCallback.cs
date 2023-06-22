namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.Input.Mouse;

public record struct MouseUserCallback(MouseEventMask TriggerMask, ushort Segment, ushort Offset);