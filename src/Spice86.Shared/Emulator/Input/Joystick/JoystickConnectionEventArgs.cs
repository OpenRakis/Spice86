namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Raised by the UI layer when a virtual stick is connected or
/// disconnected (device hot-plug, profile change, or simulated
/// disconnect from MCP/scripted tests).
/// </summary>
/// <param name="StickIndex">Zero-based stick index (0 = stick A,
/// 1 = stick B).</param>
/// <param name="IsConnected"><see langword="true"/> when the
/// stick is now plugged in.</param>
/// <param name="DeviceName">Friendly device name for diagnostics
/// and logging (e.g. <c>"Xbox 360 Controller"</c>). Empty string
/// when disconnected.</param>
public readonly record struct JoystickConnectionEventArgs(
    int StickIndex,
    bool IsConnected,
    string DeviceName);
