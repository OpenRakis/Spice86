namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

/// <summary>
/// A representation of the keyboard controller input as a stream of ushort values, for DOS.
/// </summary>
public class KeyboardStreamedInput {
    private readonly KeyboardInt16Handler _keyboardInt16Handler;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="keyboardInt16Handler">The keyboard controller</param>
    public KeyboardStreamedInput(KeyboardInt16Handler keyboardInt16Handler) {
        _keyboardInt16Handler = keyboardInt16Handler;
    }

    /// <summary>
    /// Gets whether the keyboard buffer has pending keycode data.
    /// </summary>
    /// <returns><c>True</c> if the keyboard has pending input, <c>False</c> otherwise.</returns>
    public bool HasInput => _keyboardInt16Handler.HasKeyCodePending();

    /// <summary>
    /// Returns the next pending key code.
    /// </summary>
    /// <returns>The next pending keycode.</returns>
    public ushort GetPendingInput() {
        return _keyboardInt16Handler.GetNextKeyCode() ?? 0;
    }
}