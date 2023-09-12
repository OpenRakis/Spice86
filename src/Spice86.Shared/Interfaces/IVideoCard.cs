namespace Spice86.Shared.Interfaces;

/// <summary>
/// Interface representing a video card for a computer system.
/// </summary>
public interface IVideoCard {
    /// <summary>
    /// Method to update the screen with the contents of the video memory.
    /// </summary>
    void UpdateScreen();
}