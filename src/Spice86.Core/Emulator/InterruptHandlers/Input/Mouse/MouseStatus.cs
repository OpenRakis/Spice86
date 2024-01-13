namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

/// <summary>
///    Mouse status
/// </summary>
/// <param name="X">The X axis position</param>
/// <param name="Y">They Y axis position</param>
/// <param name="ButtonFlags">The state of the button flags</param>
public record struct MouseStatus(int X, int Y, int ButtonFlags) {
    /// <inheritdoc />
    public override string ToString() {
        return string.Format("x = {0}, y = {1}, leftButton = {2}, rightButton = {3}, middleButton = {4}"
            , X, Y, (ButtonFlags & 1) == 1 ? "down" : "up", (ButtonFlags & 2) == 2 ? "down" : "up", (ButtonFlags & 4) == 4 ? "down" : "up");
    }
}