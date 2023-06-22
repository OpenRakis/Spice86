namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

/// <summary>
///     Mouse driver interface.
/// </summary>
public interface IMouseDriver {
    /// <summary>
    ///     Get the amount of buttons the mouse reports.
    /// </summary>
    ushort ButtonCount { get; }

    /// <summary>
    ///     Get the type of mouse.
    /// </summary>
    MouseType MouseType { get; }

    /// <summary>
    ///     Get or set the lower bound of the mouse horizontal position.
    /// </summary>
    ushort CurrentMinX { get; set; }

    /// <summary>
    ///     Get or set the upper bound of the mouse horizontal position.
    /// </summary>
    ushort CurrentMaxX { get; set; }

    /// <summary>
    ///     Get or set the upper bound of the mouse vertical position.
    /// </summary>
    ushort CurrentMaxY { get; set; }

    /// <summary>
    ///     Get or set the lower bound of the mouse vertical position.
    /// </summary>
    ushort CurrentMinY { get; set; }

    /// <summary>
    ///     Set the number of mickeys per pixel of horizontal movement.
    /// </summary>
    /// <value></value>
    ushort HorizontalMickeysPerPixel { set; }

    /// <summary>
    ///     Set the number of mickeys per pixel of vertical movement.
    /// </summary>
    ushort VerticalMickeysPerPixel { set; }

    /// <summary>
    ///     Set the threshold of movement amount to enable double speed.
    /// </summary>
    ushort DoubleSpeedThreshold { set; }

    /// <summary>
    ///     Get the x, y position of the mouse as well as the button flags.
    /// </summary>
    MouseStatus GetCurrentMouseStatus();

    /// <summary>
    ///     Process the mouse input.
    /// </summary>
    void Update();

    /// <summary>
    ///     Get the user-defined mouse callback.
    /// </summary>
    /// <returns></returns>
    MouseUserCallback GetRegisteredCallback();

    /// <summary>
    ///     Register a user-defined mouse callback.
    /// </summary>
    /// <param name="callbackInfo">Trigger and address of function</param>
    void RegisterCallback(MouseUserCallback callbackInfo);

    /// <summary>
    ///     Enable the default mouse cursor.
    /// </summary>
    void ShowMouseCursor();

    /// <summary>
    ///     Disable the default mouse cursor.
    /// </summary>
    void HideMouseCursor();

    /// <summary>
    ///     Set the position of the mouse cursor.
    /// </summary>
    /// <param name="x">Horizontal position in pixels</param>
    /// <param name="y">Vertical position in pixels</param>
    void SetCursorPosition(ushort x, ushort y);

    /// <summary>
    ///     Restores the registers that were saved by the driver before calling user code.
    /// </summary>
    void RestoreRegisters();
}