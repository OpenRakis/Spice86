namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Mouse;

using System;

/// <summary>
/// Events fired by the UI layer to communicate mouse actions to the emulator. <br/>
/// Also defines methods and properties for the emulator to interact with the UI layer's mouse.
/// </summary>
public interface IGuiMouseEvents {
    /// <summary>
    /// Shows the UI mouse cursor
    /// </summary>
    void ShowMouseCursor();

    /// <summary>
    /// Hides the UI mouse cursor
    /// </summary>
    void HideMouseCursor();

    /// <summary>
    /// X coordinates of the mouse cursor, in pixels.
    /// </summary>
    double MouseX { get; set; }

    /// <summary>
    /// Y coordinates of the mouse cursor, in pixels.
    /// </summary>
    double MouseY { get; set; }

    /// <summary>
    /// Fired when the mouse has moved.
    /// </summary>
    event EventHandler<MouseMoveEventArgs>? MouseMoved;

    /// <summary>
    /// Fired when a mouse button has been pressed.
    /// </summary>
    event EventHandler<MouseButtonEventArgs>? MouseButtonDown;

    /// <summary>
    /// Fired when a mouse button has been released.
    /// </summary>
    event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
}