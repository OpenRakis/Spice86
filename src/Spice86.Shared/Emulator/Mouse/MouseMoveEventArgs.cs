namespace Spice86.Shared.Emulator.Mouse;

public class MouseMoveEventArgs : EventArgs {
    public MouseMoveEventArgs(double x, double y) {
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}