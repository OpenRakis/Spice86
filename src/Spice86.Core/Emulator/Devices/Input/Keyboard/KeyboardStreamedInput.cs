namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

internal class KeyboardStreamedInput : IKeyboardStreamedInput {
    private KeyboardInt16Handler _keyboardInt16Handler;
    public KeyboardStreamedInput(KeyboardInt16Handler keyboardInt16Handler) {
        _keyboardInt16Handler = keyboardInt16Handler;
    }

    public bool HasInput => _keyboardInt16Handler.HasKeyCodePending();
    
    public ushort GetPendingInput() {
        return _keyboardInt16Handler.GetNextKeyCode() ?? 0;
    }
}