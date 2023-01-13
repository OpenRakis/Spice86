namespace Spice86.Shared.Interfaces;

using Spice86.Shared;
using Spice86.Shared.Video;

using System.Collections.Generic;

/// <summary>
/// GUI of the emulator.<br/>
/// Displays the content of the video ram (when the emulator requests it) <br/>
/// Communicates keyboard and mouse events to the emulator <br/>
/// This is the MainWindowViewModel.
/// </summary>
public interface IGui {
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
    /// When True, the Play button is displayed <br/>
    /// When False, the Pause button is displayed <br/>
    /// </summary>
    bool IsPaused { get; set; }

    /// <summary>
    /// Indicates whether a keyboard key is up.
    /// </summary>
    public event EventHandler<EventArgs>? KeyUp;

    /// <summary>
    /// Indicates whether a keyboard key is down.
    /// </summary>
    public event EventHandler<EventArgs>? KeyDown;

    /// <summary>
    /// Pauses the Emulator, displays the Play button.
    /// </summary>
    public void Pause();

    /// <summary>
    /// Plays the Emulator, displays the Pause button.
    /// </summary>
    public void Play();

    /// <summary>
    /// Whether the GUI wants to pause the emulation on startup
    /// </summary>
    bool PauseEmulatorOnStart { get; }

    /// <summary>
    /// For when the GUI wants to pause the emulation on startup, the emulator calls this to pause the thread.
    /// </summary>
    void PauseEmulationOnStart();

    /// <summary>
    /// Blocks the current thread until the GUI's WaitHandle receives a signal.
    /// </summary>
    void WaitForContinue();

    /// <summary>
    /// Removes a videobuffer from the GUI.
    /// </summary>
    /// <param name="address">The start address in memory of the video buffer.</param>
    /// <exception cref="InvalidOperationException">No buffer entry has that address.</exception>
    void RemoveBuffer(uint address);

    /// <summary>
    /// Adds a videobuffer to the GUI.
    /// </summary>
    /// <param name="videoCard">The video card implementation to use</param>
    /// <param name="address">The start address in memory of the videobuffer.</param>
    /// <param name="scale">The amount by which to scale up the videobuffer on screen.</param>
    /// <param name="bufferWidth">The width, in pixels.</param>
    /// <param name="bufferHeight">The height, in pixels.</param>
    /// <param name="isPrimaryDisplay">Indicates if this is the videoBuffer that will receive input (mouse, keyboard, gamepad) events. False by default.</param>
    void AddBuffer(IVideoCard videoCard, uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false);

    IDictionary<uint, IVideoBufferViewModel> VideoBuffersToDictionary { get; }

    /// <summary>
    /// X coordinates of the mouse cursor, in pixels.
    /// </summary>
    int MouseX { get; set; }

    /// <summary>
    /// Y coordinates of the mouse cursor, in pixels.
    /// </summary>
    int MouseY { get; set; }

    /// <summary>
    /// On video mode change: Set Resolution of the video source for the GUI to display
    /// </summary>
    /// <param name="videoMode">The video mode. Width, Height, main memory mapping physical address, etc...</param>
    void SetVideoMode(VideoModeBase videoMode);

    /// <summary>
    /// Indicates whether the LMB is down.
    /// </summary>
    bool IsLeftButtonClicked { get; }

    /// <summary>
    /// Indicates whether the RMB is down.
    /// </summary>
    bool IsRightButtonClicked { get; }

    /// <summary>
    /// Width of the primary video buffer from the emulator's point of view, in pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Height of the primary video buffer from the emulator's point of view, in pixels.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Refresh the display with the content of the video ram.
    /// </summary>
    void UpdateScreen();
}
