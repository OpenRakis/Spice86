namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Debugger;

/// <summary>
/// Interface representing a video card for a computer system.
/// </summary>
public interface IVideoCard : IDebuggableComponent {
    /// <summary>
    /// Method to update the screen with the contents of the video memory.
    /// </summary>
    void UpdateScreen();
}