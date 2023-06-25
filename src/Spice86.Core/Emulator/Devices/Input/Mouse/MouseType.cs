namespace Spice86.Core.Emulator.Devices.Input.Mouse;

/// <summary>
///     The type of mouse.
/// </summary>
public enum MouseType {
    /// <summary>
    ///     No mouse.
    /// </summary>
    None,

    /// <summary>
    ///     PS/2 mouse (with 3 buttons)
    /// </summary>
    Ps2,

    /// <summary>
    ///     PS/2 mouse with wheel (and 3 buttons) TODO: not implemented
    /// </summary>
    Ps2Wheel,

    /// <summary>
    ///     Microsoft InPort mouse TODO: not implemented
    /// </summary>
    InPort,

    /// <summary>
    ///     Serial port mouse TODO: not implemented
    /// </summary>
    Serial,

    /// <summary>
    ///     Bus mouse TODO: not implemented
    /// </summary>
    Bus
}