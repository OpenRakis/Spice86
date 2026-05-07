namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Immutable snapshot of the full two-stick gameport input layer.
/// Built up on the emulator thread from <c>IGuiJoystickEvents</c>
/// drained out of <c>InputEventHub</c>, and consumed by the Core
/// <c>Gameport</c> device on every port-<c>0x201</c> access.
/// </summary>
/// <param name="StickA">State of the first virtual stick (DOSBox
/// Staging <c>stick[0]</c>).</param>
/// <param name="StickB">State of the second virtual stick (DOSBox
/// Staging <c>stick[1]</c>).</param>
public readonly record struct VirtualJoystickState(
    VirtualStickState StickA,
    VirtualStickState StickB) {

    /// <summary>
    /// A pair of disconnected sticks. Equivalent to "no joysticks
    /// plugged in" and causes port <c>0x201</c> to read <c>0xFF</c>.
    /// </summary>
    public static VirtualJoystickState Disconnected { get; } =
        new(VirtualStickState.Disconnected, VirtualStickState.Disconnected);
}
