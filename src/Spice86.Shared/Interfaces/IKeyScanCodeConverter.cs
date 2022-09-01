namespace Spice86.Shared.Interfaces;
public interface IKeyScanCodeConverter {

    public byte? GetAsciiCode(byte scancode);

    public byte? GetKeyPressedScancode(KeyboardInput keyCode);

    public byte? GetKeyReleasedScancode(KeyboardInput keyCode);
}
