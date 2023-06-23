namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;

/// <summary>
///     Interface for mouse devices.
/// </summary>
public interface IMouseDevice : IIOPortHandler {
    /// <summary>
    ///     The type of mouse.
    /// </summary>
    MouseType MouseType { get; }

    /// <summary>
    ///     Whether the left mouse button is currently pressed.
    /// </summary>
    bool IsLeftButtonDown { get; }

    /// <summary>
    ///     Whether the right mouse button is currently pressed.
    /// </summary>
    bool IsRightButtonDown { get; }

    /// <summary>
    ///     Whether the middle mouse button is currently pressed.
    /// </summary>
    bool IsMiddleButtonDown { get; }

    /// <summary>
    ///     The threshold of movement amount to enable double speed.
    /// </summary>
    int DoubleSpeedThreshold { get; set; }

    /// <summary>
    ///     The number of mickeys per pixel of horizontal movement.
    /// </summary>
    int HorizontalMickeysPerPixel { get; set; }

    /// <summary>
    ///     The number of mickeys per pixel of vertical movement.
    /// </summary>
    int VerticalMickeysPerPixel { get; set; }

    /// <summary>
    ///     A bitmask of the mouse events that were triggered since the last update.
    /// </summary>
    MouseEventMask LastTrigger { get; }

    /// <summary>
    ///     The sample rate of the mouse.
    /// </summary>
    int SampleRate { get; set; }

    /// <summary>
    ///     The amount of movement in the Y direction since the last update.
    /// </summary>
    double DeltaY { get; }

    /// <summary>
    ///     The amount of movement in the X direction since the last update.
    /// </summary>
    double DeltaX { get; }

    /// <summary>
    ///     The number of buttons on the mouse.
    /// </summary>
    int ButtonCount { get; }

    /// <summary>
    ///     Horizontal position of the mouse cursor relative to the window.
    /// </summary>
    double MouseXRelative { get; set; }

    /// <summary>
    ///     Vertical position of the mouse cursor relative to the window.
    /// </summary>
    double MouseYRelative { get; set; }
}