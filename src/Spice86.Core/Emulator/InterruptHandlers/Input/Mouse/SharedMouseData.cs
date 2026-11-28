namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
/// <summary>
///     Provides access to the current emulated mouse status for the GUI.
///     This holder resolves the circular dependency between the GUI and the mouse device and driver.
/// </summary>
public class SharedMouseData {
    /// <summary>
    ///     Horizontal position of the mouse cursor relative to the window.
    /// </summary>
    public double MouseXRelative { get; set; }

    /// <summary>
    ///     Vertical position of the mouse cursor relative to the window.
    /// </summary>
    public double MouseYRelative { get; set; }

    /// <summary>
    ///     Get or set the lower bound of the mouse horizontal position.
    /// </summary>
    public int CurrentMinX { get; set; }

    /// <summary>
    ///     Get or set the upper bound of the mouse horizontal position.
    /// </summary>
    public int CurrentMaxX { get; set; }

    /// <summary>
    ///     Get or set the upper bound of the mouse vertical position.
    /// </summary>
    public int CurrentMaxY { get; set; }

    /// <summary>
    ///     Get or set the lower bound of the mouse vertical position.
    /// </summary>
    public int CurrentMinY { get; set; }

    /// <summary>
    ///     Whether the left mouse button is currently pressed.
    /// </summary>
    public bool IsLeftButtonDown { get; set; }

    /// <summary>
    ///     Whether the right mouse button is currently pressed.
    /// </summary>
    public bool IsRightButtonDown { get; set; }

    /// <summary>
    ///     Whether the middle mouse button is currently pressed.
    /// </summary>
    public bool IsMiddleButtonDown { get; set; }

    /// <summary>
    ///     Gets the current emulated mouse status including position and button states.
    /// </summary>
    public MouseStatusRecord CurrentMouseStatus {
        get {
            int x = LinearInterpolate(MouseXRelative, CurrentMinX, CurrentMaxX);
            int y = LinearInterpolate(MouseYRelative, CurrentMinY, CurrentMaxY);
            ushort buttonFlags = (ushort)((IsLeftButtonDown ? 1 : 0) | (IsRightButtonDown ? 2 : 0) | (IsMiddleButtonDown ? 4 : 0));
            return new MouseStatusRecord(x, y, buttonFlags);
        }
    }

    private static int LinearInterpolate(double index, int min, int max) {
        double clamped = Math.Clamp(index, 0.0, 1.0);
        return (int)(min + ((max - min) * clamped));
    }
}