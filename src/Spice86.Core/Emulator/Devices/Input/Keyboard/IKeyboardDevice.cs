namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;

/// <summary>
/// Basic interface for a keyboard
/// </summary>
public interface IKeyboardDevice {
    KeyboardEventArgs Input { get;  }
}