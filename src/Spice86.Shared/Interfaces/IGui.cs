namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;

/// <summary>
/// GUI of the emulator.<br/>
/// Displays the content of the video ram (when the emulator requests it) <br/>
/// Communicates keyboard and mouse events to the emulator <br/>
/// This is the MainWindowViewModel.
/// </summary>
public interface IGui {
    /// <summary>
    /// Whether the mouse cursor is shown.
    /// </summary>
    bool ShowCursor { get; set; }

    /// <summary>
    /// Shows the UI mouse cursor
    /// </summary>
    void ShowMouseCursor();

    /// <summary>
    /// Hides the UI mouse cursor
    /// </summary>
    void HideMouseCursor();

    /// <summary>
    /// Indicates whether the GUI considers the Emulator is paused. <br/>
    /// When <c>true</c>, the Play button is displayed <br/>
    /// When <c>false</c>, the Pause button is displayed <br/>
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Makes the UI display the Pause button, and hide the Pause button.
    /// </summary>
    void Play();

    /// <summary>
    /// Indicates whether a keyboard key is up.
    /// </summary>
    public event EventHandler<KeyboardEventArgs>? KeyUp;

    /// <summary>
    /// Indicates whether a keyboard key is down.
    /// </summary>
    public event EventHandler<KeyboardEventArgs>? KeyDown;

    /// <summary>
    /// X coordinates of the mouse cursor, in pixels.
    /// </summary>
    double MouseX { get; set; }

    /// <summary>
    /// Y coordinates of the mouse cursor, in pixels.
    /// </summary>
    double MouseY { get; set; }

    /// <summary>
    /// Indicates whether the LMB is down.
    /// </summary>
    bool IsLeftButtonClicked { get; }

    /// <summary>
    /// Indicates whether the RMB is down.
    /// </summary>
    bool IsRightButtonClicked { get; }

    /// <summary>
    /// Width of the video display from the emulator's point of view, in pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Height of the video display from the emulator's point of view, in pixels.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Refresh the display with the content of the video ram.
    /// </summary>
    void UpdateScreen();

    /// <summary>
    /// On video mode change: Set Resolution of the video source for the GUI to display
    /// </summary>
    /// <param name="videoWidth">The width in pixels</param>
    /// <param name="videoHeight">The height in pixels</param>
    void SetResolution(int videoWidth, int videoHeight);

    /// <summary>
    /// Indicate that the mouse has moved.
    /// </summary>
    event EventHandler<MouseMoveEventArgs>? MouseMoved;

    /// <summary>
    /// Indicate that a mouse button has been pressed.
    /// </summary>
    event EventHandler<MouseButtonEventArgs>? MouseButtonDown;

    /// <summary>
    /// Indicate that a mouse button has been released.
    /// </summary>
    event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
}