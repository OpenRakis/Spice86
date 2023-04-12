namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Keyboard;

public interface IKeyScanCodeConverter {
    public byte? GetAsciiCode(byte scancode);

    public byte? GetKeyPressedScancode(KeyboardInput keyCode);

    public byte? GetKeyReleasedScancode(KeyboardInput keyCode);
}
