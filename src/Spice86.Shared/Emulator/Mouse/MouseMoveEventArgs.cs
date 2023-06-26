namespace Spice86.Shared.Emulator.Mouse;

/// <summary>
/// Contains the details of a mouse movement event.
/// </summary>
public readonly record struct MouseMoveEventArgs {
    /// <summary>
    /// Instantiates a new instance of <see cref="MouseMoveEventArgs"/>.
    /// </summary>
    /// <param name="x">The horizontal mouse position on a scale from 0 to 1</param>
    /// <param name="y">The vertical mouse position on a scale from 0 to 1</param>
    public MouseMoveEventArgs(double x, double y) {
        X = x;
        Y = y;
    }

    /// <summary>
    /// The horizontal mouse position on a scale from 0 to 1.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// The vertical mouse position on a scale from 0 to 1.
    /// </summary>
    public double Y { get; }
}