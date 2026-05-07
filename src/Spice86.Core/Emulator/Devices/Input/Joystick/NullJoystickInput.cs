namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// <see cref="IGameportInputSource"/> that always reports a
/// disconnected pair of sticks. Used in headless mode and as a
/// safe default when no UI/SDL adapter is available, so that the
/// Core <see cref="Gameport"/> device can always be constructed.
/// </summary>
public sealed class NullJoystickInput : IGameportInputSource {

    /// <inheritdoc />
    public string DisplayName => "None (no joystick)";

    /// <inheritdoc />
    public VirtualJoystickState GetCurrentState() {
        return VirtualJoystickState.Disconnected;
    }
}
