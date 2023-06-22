namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

public record struct MouseStatus(ushort X, ushort Y, ushort ButtonFlags) {
    /// <inheritdoc />
    public override string ToString() {
        return string.Format("x = {0}, y = {1}, , leftButton = {2}, rightButton = {3}, middleButton = {4}"
            , X, Y, (ButtonFlags & 1) == 1 ? "down" : "up", (ButtonFlags & 2) == 2 ? "down" : "up", (ButtonFlags & 4) == 4 ? "down" : "up");
    }
}