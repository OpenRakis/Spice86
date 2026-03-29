namespace Spice86.Core.Emulator.Devices.Input.Joystick;

/// <summary>
/// Immutable snapshot of the full gameport state: the raw I/O port 0x201 byte and both joystick states.
/// Used for thread-safe UI diagnostics.
/// </summary>
/// <param name="PortValue">The raw byte value from reading I/O port 0x201.</param>
/// <param name="JoystickA">Snapshot of joystick A state.</param>
/// <param name="JoystickB">Snapshot of joystick B state.</param>
public readonly record struct JoystickPortSnapshot(
    byte PortValue, JoystickSnapshot JoystickA, JoystickSnapshot JoystickB);
