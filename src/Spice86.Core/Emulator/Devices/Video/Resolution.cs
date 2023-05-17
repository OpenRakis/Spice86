namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// Represents a resolution.
/// </summary>
public struct Resolution {
    /// <summary>
    /// The width of the resolution.
    /// </summary>
    public int Width;

    /// <summary>
    /// The height of the resolution.
    /// </summary>
    public int Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        if (obj is not Resolution other) {
            return false;
        }

        return Width == other.Width && Height == other.Height;
    }

    /// <summary>
    ///   Compare two resolutions for equality.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool Equals(Resolution other) {
        return Width == other.Width && Height == other.Height;
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return HashCode.Combine(Width, Height);
    }

    /// <summary>
    ///    Compare two resolutions for equality.
    /// </summary>
    public static bool operator ==(Resolution left, Resolution right) {
        return left.Equals(right);
    }

    /// <summary>
    ///   Compare two resolutions for inequality.
    /// </summary>
    public static bool operator !=(Resolution left, Resolution right) {
        return !(left == right);
    }
}