namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Shared.Emulator.Mouse;

/// <summary>
///     Mouse driver interface.
/// </summary>
public interface IMouseDriver : IAssemblyRoutineWriter {
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
    /// <returns>A mutable struct with value based equality representing the mouse user callback function.</returns>
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
    void AfterUserHandlerExecution();

    /// <summary>
    ///     Get the number of mickeys of horizontal movement.
    /// </summary>
    short GetDeltaXMickeys();

    /// <summary>
    ///     Get the number of mickeys of vertical movement.
    /// </summary>
    short GetDeltaYMickeys();

    /// <summary>
    /// Gets the count of button presses since the last call.
    /// </summary>
    /// <param name="button">The button to query (0=left, 1=right, 2=center).</param>
    /// <returns>The count of button presses.</returns>
    int GetButtonPressCount(MouseButton button);

    /// <summary>
    /// Gets the last X position of the mouse when the button was pressed.
    /// </summary>
    /// <param name="button">0= Left, 1= right, 2= center</param>
    /// <returns>The X part of the virtual coordinates.</returns>
    double GetLastPressedX(MouseButton button);

    /// <summary>
    /// Gets the last Y position of the mouse when the button was pressed.
    /// </summary>
    /// <param name="button">0= Left, 1= right, 2= center</param>
    /// <returns>The Y part of the virtual coordinates.</returns>
    double GetLastPressedY(MouseButton button);

    double GetLastReleasedX(MouseButton button);
    double GetLastReleasedY(MouseButton button);
    int GetButtonsReleaseCount(MouseButton button);

    /// <summary>
    /// Retrieves the X-coordinate of the last position where the specified mouse button was released.
    /// </summary>
    /// <param name="button">The mouse button for which the last released X-coordinate is requested.</param>
    /// <returns>The X-coordinate of the last release position for the specified mouse button.  Returns 0 if the button has not
    /// been released or if no data is available.</returns>
    double GetLastReleasedX(MouseButton button);

    /// <summary>
    /// Retrieves the Y-coordinate of the last release event for the specified mouse button.
    /// </summary>
    /// <param name="button">The mouse button for which to retrieve the last release Y-coordinate.</param>
    /// <returns>The Y-coordinate of the last release event for the specified mouse button. Returns 0 if the button has not been
    /// released or if no data is available.</returns>
    double GetLastReleasedY(MouseButton button);

    /// <summary>
    /// Retrieves the number of times the specified mouse button was released since the last call to this method.
    /// </summary>
    /// <remarks>After calling this method, the release count for the specified mouse button is reset to
    /// 0.</remarks>
    /// <param name="button">The mouse button for which the release count is requested.</param>
    /// <returns>The number of times the specified mouse button was released. Returns 0 if the button has not been released or if
    /// no data is available.</returns>
    int GetButtonsReleaseCount(MouseButton button);

    /// <summary>
    ///     Resets the mouse driver to default values.
    /// </summary>
    void Reset();
}