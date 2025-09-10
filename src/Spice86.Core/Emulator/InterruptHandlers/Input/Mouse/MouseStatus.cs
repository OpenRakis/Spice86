namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

/// <summary>
///    Mouse status
/// </summary>
/// <param name="X">The X axis position</param>
/// <param name="Y">They Y axis position</param>
/// <param name="ButtonFlags">The state of the button flags</param>
public readonly record struct MouseStatus(int X, int Y, int ButtonFlags) {
    /// <inheritdoc />
    public override string ToString() {
        return
            $"x = {X}, y = {Y}, leftButton = {((ButtonFlags & 1) == 1 ? "down" : "up")}, rightButton = {((ButtonFlags & 2) == 2 ? "down" : "up")}, middleButton = {((ButtonFlags & 4) == 4 ? "down" : "up")}";
    }
}