namespace Spice86.Core.Emulator.Devices.Input.Mouse;

/// <summary>
///     Bit flags representing mouse events.
/// </summary>
[Flags]
public enum MouseEventMask {
    /// <summary>
    ///     The mouse was moved.
    /// </summary>
    Movement = 1 << 0,

    /// <summary>
    ///     The left mouse button was pressed.
    /// </summary>
    LeftButtonDown = 1 << 1,

    /// <summary>
    ///     The left mouse button was released.
    /// </summary>
    LeftButtonUp = 1 << 2,

    /// <summary>
    ///     The right mouse button was pressed.
    /// </summary>
    RightButtonDown = 1 << 3,

    /// <summary>
    ///     The right mouse button was released.
    /// </summary>
    RightButtonUp = 1 << 4,

    /// <summary>
    ///     The middle mouse button was pressed.
    /// </summary>
    MiddleButtonDown = 1 << 5,

    /// <summary>
    ///     The middle mouse button was released.
    /// </summary>
    MiddleButtonUp = 1 << 6
}