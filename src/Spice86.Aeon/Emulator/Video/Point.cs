namespace Spice86.Aeon.Emulator.Video; 

/// <summary>
/// Represents a point on an emulated buffer from the top-left corner.
/// </summary>
public struct Point : IEquatable<Point>
{
    /// <summary>
    /// Initializes a new Point struct.
    /// </summary>
    /// <param name="x">Zero-based distance of the point from the left side of the buffer.</param>
    /// <param name="y">Zero-based distance of the point from the top of the buffer.</param>
    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static bool operator ==(Point pointA, Point pointB) => pointA.Equals(pointB);
    public static bool operator !=(Point pointA, Point pointB) => !pointA.Equals(pointB);

    /// <summary>
    /// Gets or sets the zero-based distance of the point from the left side of the buffer.
    /// </summary>
    public int X { readonly get; set; }
    /// <summary>
    /// Gets or sets the zero-based distance of the point from the top of the buffer.
    /// </summary>
    public int Y { readonly get; set; }

    /// <summary>
    /// Gets a string representation of the Point.
    /// </summary>
    /// <returns>String representation of the Point.</returns>
    public override readonly string ToString() => $"{X}, {Y}";
    /// <summary>
    /// Tests for equality with another Point.
    /// </summary>
    /// <param name="other">Other Point to test.</param>
    /// <returns>True if points are equal; otherwise false.</returns>
    public readonly bool Equals(Point other) => X == other.X && Y == other.Y;
    /// <summary>
    /// Tests for equality with another object.
    /// </summary>
    /// <param name="obj">Object to test.</param>
    /// <returns>True if objects are equal; otherwise false.</returns>
    public override readonly bool Equals(object? obj) => obj is Point p && Equals(p);
    /// <summary>
    /// Gets a hash code for the point.
    /// </summary>
    /// <returns>Hash code for the point.</returns>
    public override readonly int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();
}