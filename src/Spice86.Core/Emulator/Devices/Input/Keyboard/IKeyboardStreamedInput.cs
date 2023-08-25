namespace Spice86.Core.Emulator.Devices.Input.Keyboard; 

public interface IKeyboardStreamedInput {
    bool HasInput { get; }
    ushort GetPendingInput();
}