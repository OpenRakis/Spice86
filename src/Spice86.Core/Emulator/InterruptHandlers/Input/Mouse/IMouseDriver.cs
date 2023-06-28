namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.Input.Mouse;

/// <summary>
///     Mouse driver interface.
/// </summary>
public interface IMouseDriver {
    /// <summary>
    ///     Get the amount of buttons the mouse reports.
    /// </summary>
    int ButtonCount { get; }

    /// <summary>
    ///     Get the type of mouse.
    /// </summary>
    MouseType MouseType { get; }

    /// <summary>
    ///     Get or set the lower bound of the mouse horizontal position.
    /// </summary>
    int CurrentMinX { get; set; }

    /// <summary>
    ///     Get or set the upper bound of the mouse horizontal position.
    /// </summary>
    int CurrentMaxX { get; set; }

    /// <summary>
    ///     Get or set the upper bound of the mouse vertical position.
    /// </summary>
    int CurrentMaxY { get; set; }

    /// <summary>
    ///     Get or set the lower bound of the mouse vertical position.
    /// </summary>
    int CurrentMinY { get; set; }

    /// <summary>
    ///     Set the number of mickeys per pixel of horizontal movement.
    /// </summary>
    /// <value></value>
    int HorizontalMickeysPerPixel { get; set; }

    /// <summary>
    ///     Set the number of mickeys per pixel of vertical movement.
    /// </summary>
    int VerticalMickeysPerPixel { get; set; }

    /// <summary>
    ///     Set the threshold of movement amount to enable double speed.
    /// </summary>
    int DoubleSpeedThreshold { get; set; }

    /// <summary>
    ///     Set the handler for Mouse User routine manipulation.
    /// </summary>
    IAsmUserRoutineHandler? UserRoutineHandler { set; }

    /// <summary>
    ///     Get the x, y position of the mouse as well as the button flags.
    /// </summary>
    MouseStatus GetCurrentMouseStatus();

    /// <summary>
    ///     Process the mouse input.
    /// </summary>
    void BeforeUserHandlerExecution();

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
    void SetCursorPosition(int x, int y);

    /// <summary>
    ///     Restores the registers that were saved by the driver before calling user code.
    /// </summary>
    void AfterMouseDriverExecution();

    /// <summary>
    ///     Get the number of mickeys of horizontal movement.
    /// </summary>
    short GetDeltaXMickeys();

    /// <summary>
    ///     Get the number of mickeys of vertical movement.
    /// </summary>
    /// <returns></returns>
    short GetDeltaYMickeys();

    /// <summary>
    ///     Resets the mouse driver to default values.
    /// </summary>
    void Reset();
}