namespace Spice86.Core.Emulator.Devices.Video;

public struct Resolution {
    public int Width;
    public int Height;
    
    // override object.Equals
    public override bool Equals(object? obj) {
        if (obj is not Resolution other) {
            return false;
        }

        return Width == other.Width && Height == other.Height;
    }

    public bool Equals(Resolution other) {
        return Width == other.Width && Height == other.Height;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Width, Height);
    }

    public static bool operator ==(Resolution left, Resolution right) {
        return left.Equals(right);
    }

    public static bool operator !=(Resolution left, Resolution right) {
        return !(left == right);
    }
}