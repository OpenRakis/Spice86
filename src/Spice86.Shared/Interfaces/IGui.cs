namespace Spice86.Shared.Interfaces;

/// <summary>
/// GUI of the emulator.<br/>
/// Displays the content of the video ram (when the emulator requests it) <br/>
/// Communicates keyboard and mouse events to the emulator <br/>
/// This is the MainWindowViewModel.
/// </summary>
public interface IGui : IGuiKeyboardEvents, IGuiMouseEvents, IGuiVideoPresentation {
}