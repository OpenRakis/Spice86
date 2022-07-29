namespace Spice86.UI.Interfaces;

using Spice86.UI.Keyboard;

public interface IKeyScanCodeConverter {

    public byte? GetAsciiCode(byte scancode);

    public byte? GetKeyPressedScancode(KeyboardInput keyCode);

    public byte? GetKeyReleasedScancode(KeyboardInput keyCode);
}
