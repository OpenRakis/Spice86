namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Identifies which logical virtual axis a raw SDL axis (or other
/// physical input) drives. The Core <c>Gameport</c> only ever sees
/// these logical names; the JSON mapper layer translates raw SDL
/// indices into <see cref="VirtualAxis"/> values.
/// </summary>
public enum VirtualAxis {
    /// <summary>Stick A, X axis.</summary>
    StickAX,

    /// <summary>Stick A, Y axis.</summary>
    StickAY,

    /// <summary>Stick A, Z (throttle) axis.</summary>
    StickAZ,

    /// <summary>Stick A, R (rudder) axis.</summary>
    StickAR,

    /// <summary>Stick B, X axis.</summary>
    StickBX,

    /// <summary>Stick B, Y axis.</summary>
    StickBY,

    /// <summary>Stick B, Z axis.</summary>
    StickBZ,

    /// <summary>Stick B, R axis.</summary>
    StickBR,

    /// <summary>The axis is unmapped and should be ignored.</summary>
    None
}
