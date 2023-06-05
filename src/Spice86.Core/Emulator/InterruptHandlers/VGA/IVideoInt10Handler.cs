namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Represents the VGA interrupt interface for managing video functionality.
/// </summary>
public interface IVideoInt10Handler : ICallback {
    /// <summary>
    ///     Writes a string to the video buffer.
    /// </summary>
    void WriteString();

    /// <summary>
    ///     Sets the video mode.
    /// </summary>
    void SetVideoMode();

    /// <summary>
    ///     Retrieves information about the VGA video functionality.
    /// </summary>
    /// <returns>The information about the VGA video functionality.</returns>
    VideoFunctionalityInfo GetFunctionalityInfo();

    /// <summary>
    ///     Combines the video display.
    /// </summary>
    void GetSetDisplayCombinationCode();

    /// <summary>
    ///     Configures the video subsystem.
    /// </summary>
    void VideoSubsystemConfiguration();

    /// <summary>
    ///     Manages the character generator routine.
    /// </summary>
    void LoadFontInfo();

    /// <summary>
    ///     Gets or sets the palette registers.
    /// </summary>
    void SetPaletteRegisters();

    /// <summary>
    ///     Gets the current video mode.
    /// </summary>
    void GetVideoState();

    /// <summary>
    ///     Writes text in teletype mode.
    /// </summary>
    void WriteTextInTeletypeMode();

    /// <summary>
    ///     Sets the color palette or background color.
    /// </summary>
    void SetColorPaletteOrBackGroundColor();

    /// <summary>
    ///     Writes a character at the cursor position.
    /// </summary>
    void WriteCharacterAtCursor();

    /// <summary>
    ///     Writes a character and attribute at the cursor position.
    /// </summary>
    void WriteCharacterAndAttributeAtCursor();

    /// <summary>
    ///     Reads the character and attribute at the cursor position.
    /// </summary>
    void ReadCharacterAndAttributeAtCursor();

    /// <summary>
    ///     Scrolls the page down.
    /// </summary>
    void ScrollPageDown();

    /// <summary>
    ///     Scrolls the page up.
    /// </summary>
    void ScrollPageUp();

    /// <summary>
    ///     Selects the active display page.
    /// </summary>
    void SelectActiveDisplayPage();

    /// <summary>
    ///     Gets the current cursor position.
    /// </summary>
    void GetCursorPosition();

    /// <summary>
    ///     Sets the cursor position.
    /// </summary>
    void SetCursorPosition();

    /// <summary>
    ///     Sets the cursor type.
    /// </summary>
    void SetCursorType();

    /// <summary>
    ///     Writes a single pixel.
    /// </summary>
    void WriteDot();

    /// <summary>
    ///     Reads a single pixel.
    /// </summary>
    void ReadDot();

    /// <summary>
    ///     Reads the light pen position.
    /// </summary>
    void ReadLightPenPosition();
}