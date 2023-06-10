namespace Spice86.Shared.Interfaces;

/// <summary>
/// Interface representing a video card for a computer system.
/// </summary>
public interface IVideoCard {
    /// <summary>
    /// Method to update the screen with the contents of the video memory.
    /// </summary>
    void UpdateScreen();

    /// <summary>
    /// Method to render graphics onto the screen from video memory.
    /// </summary>
    /// <param name="buffer">The structure to load the argb pixel into</param>
    void Render(Span<uint> buffer);
}