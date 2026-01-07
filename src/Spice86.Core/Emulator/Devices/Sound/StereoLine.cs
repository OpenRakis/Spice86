namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
///     Defines how stereo channels map to output lines.
/// </summary>
public readonly struct StereoLine {
    /// <summary>
    ///     Initializes a new instance of the <see cref="StereoLine"/> struct.
    /// </summary>
    /// <param name="left">The line index for the left channel.</param>
    /// <param name="right">The line index for the right channel.</param>
    public StereoLine(LineIndex left, LineIndex right) {
        Left = left;
        Right = right;
    }

    /// <summary>
    ///     Gets the line index for the left channel.
    /// </summary>
    public LineIndex Left { get; }

    /// <summary>
    ///     Gets the line index for the right channel.
    /// </summary>
    public LineIndex Right { get; }

    /// <summary>
    ///     Standard stereo mapping: left to left, right to right.
    /// </summary>
    public static readonly StereoLine StereoMap = new(LineIndex.Left, LineIndex.Right);

    /// <summary>
    ///     Reverse stereo mapping: left to right, right to left.
    /// </summary>
    public static readonly StereoLine ReverseMap = new(LineIndex.Right, LineIndex.Left);

    /// <summary>
    ///     Determines whether two stereo lines are equal.
    /// </summary>
    /// <param name="left">The first stereo line.</param>
    /// <param name="right">The second stereo line.</param>
    /// <returns><c>true</c> if the stereo lines are equal; otherwise, <c>false</c>.</returns>
    public static bool operator ==(StereoLine left, StereoLine right) {
        return left.Left == right.Left && left.Right == right.Right;
    }

    /// <summary>
    ///     Determines whether two stereo lines are not equal.
    /// </summary>
    /// <param name="left">The first stereo line.</param>
    /// <param name="right">The second stereo line.</param>
    /// <returns><c>true</c> if the stereo lines are not equal; otherwise, <c>false</c>.</returns>
    public static bool operator !=(StereoLine left, StereoLine right) {
        return !(left == right);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        return obj is StereoLine other && this == other;
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return HashCode.Combine(Left, Right);
    }
}
