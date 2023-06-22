namespace Spice86.Core.Emulator.Devices.Input.Mouse;

[Flags]
public enum MouseEventMask {
    Movement = 1 << 0,
    LeftButtonDown = 1 << 1,
    LeftButtonUp = 1 << 2,
    RightButtonDown = 1 << 3,
    RightButtonUp = 1 << 4,
    MiddleButtonDown = 1 << 5,
    MiddleButtonUp = 1 << 6
}