namespace Spice86.Libs.Sound.Common;

/// <summary>
///     Represents a stereo audio frame with left and right channel sample values.
/// </summary>
internal struct AudioFrame : IEquatable<AudioFrame> {
    /// <summary>
    ///     Sample amplitude for the left channel.
    /// </summary>
    public float Left;

    /// <summary>
    ///     Sample amplitude for the right channel.
    /// </summary>
    public float Right;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AudioFrame" /> struct with explicit channel values.
    /// </summary>
    /// <param name="left">The left channel sample value.</param>
    /// <param name="right">The right channel sample value.</param>
    public AudioFrame(float left, float right) {
        Left = left;
        Right = right;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AudioFrame" /> struct with the same value for both channels.
    /// </summary>
    /// <param name="mono">The sample value applied to both channels.</param>
    public AudioFrame(float mono) {
        Left = mono;
        Right = mono;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AudioFrame" /> struct with 16-bit channel values.
    /// </summary>
    /// <param name="left">The 16-bit left channel sample value.</param>
    /// <param name="right">The 16-bit right channel sample value.</param>
    public AudioFrame(short left, short right) {
        Left = left;
        Right = right;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AudioFrame" /> struct with the same 16-bit value for both channels.
    /// </summary>
    /// <param name="mono">The 16-bit sample value applied to both channels.</param>
    public AudioFrame(short mono) {
        Left = mono;
        Right = mono;
    }

    /// <summary>
    ///     Gets or sets the channel sample value at the specified <paramref name="index" />.
    /// </summary>
    /// <param name="index">0 for the left channel, 1 for the right channel.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index" /> is not 0 or 1.</exception>
    public float this[int index] {
        readonly get =>
            index switch {
                0 => Left,
                1 => Right,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        set {
            switch (index) {
                case 0:
                    Left = value;
                    break;
                case 1:
                    Right = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    /// <summary>
    ///     Determines whether two audio frames contain exactly equal sample values.
    /// </summary>
    /// <param name="left">The first frame to compare.</param>
    /// <param name="right">The second frame to compare.</param>
    /// <returns><c>true</c> if both channel values are identical; otherwise, <c>false</c>.</returns>
    public static bool operator ==(AudioFrame left, AudioFrame right) {
        // ReSharper disable CompareOfFloatsByEqualityOperator
        return left.Left == right.Left && left.Right == right.Right;
        // ReSharper restore CompareOfFloatsByEqualityOperator
    }

    /// <summary>
    ///     Determines whether two audio frames contain different sample values.
    /// </summary>
    /// <param name="left">The first frame to compare.</param>
    /// <param name="right">The second frame to compare.</param>
    /// <returns><c>true</c> if any channel values differ; otherwise, <c>false</c>.</returns>
    public static bool operator !=(AudioFrame left, AudioFrame right) {
        return !(left == right);
    }

    /// <summary>
    ///     Adds the sample values of two frames channel-wise.
    /// </summary>
    /// <param name="left">The first frame.</param>
    /// <param name="right">The second frame.</param>
    /// <returns>An <see cref="AudioFrame" /> containing the summed channel values.</returns>
    public static AudioFrame operator +(AudioFrame left, AudioFrame right) {
        return new AudioFrame(left.Left + right.Left, left.Right + right.Right);
    }

    /// <summary>
    ///     Scales both channels of the frame by the specified gain factor.
    /// </summary>
    /// <param name="frame">The frame to scale.</param>
    /// <param name="gain">The gain applied to both channels.</param>
    /// <returns>An <see cref="AudioFrame" /> with scaled samples.</returns>
    public static AudioFrame operator *(AudioFrame frame, float gain) {
        return new AudioFrame(frame.Left * gain, frame.Right * gain);
    }

    /// <summary>
    ///     Scales both channels of the frame by the specified gain factor.
    /// </summary>
    /// <param name="gain">The gain applied to both channels.</param>
    /// <param name="frame">The frame to scale.</param>
    /// <returns>An <see cref="AudioFrame" /> with scaled samples.</returns>
    public static AudioFrame operator *(float gain, AudioFrame frame) {
        return frame * gain;
    }

    /// <summary>
    ///     Multiplies the channels of the frame by the corresponding channels of a gain frame.
    /// </summary>
    /// <param name="frame">The frame to scale.</param>
    /// <param name="gain">The per-channel gain.</param>
    /// <returns>An <see cref="AudioFrame" /> with scaled samples.</returns>
    public static AudioFrame operator *(AudioFrame frame, AudioFrame gain) {
        return new AudioFrame(frame.Left * gain.Left, frame.Right * gain.Right);
    }

    /// <summary>
    ///     Adds the sample values of this frame to another frame channel-wise.
    /// </summary>
    /// <param name="other">The frame to add.</param>
    /// <returns>An <see cref="AudioFrame" /> containing the summed channel values.</returns>
    public AudioFrame Add(AudioFrame other) {
        return this + other;
    }

    /// <summary>
    ///     Scales both channels of this frame by the specified gain factor.
    /// </summary>
    /// <param name="gain">The gain applied to both channels.</param>
    /// <returns>An <see cref="AudioFrame" /> with scaled samples.</returns>
    public AudioFrame Multiply(float gain) {
        return this * gain;
    }

    /// <summary>
    ///     Multiplies the channels of this frame by the corresponding channels of a gain frame.
    /// </summary>
    /// <param name="gain">The per-channel gain.</param>
    /// <returns>An <see cref="AudioFrame" /> with scaled samples.</returns>
    public AudioFrame Multiply(AudioFrame gain) {
        return this * gain;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        return obj is AudioFrame other && this == other;
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return HashCode.Combine(Left, Right);
    }

    /// <inheritdoc />
    public bool Equals(AudioFrame other) {
        return Left.Equals(other.Left) && Right.Equals(other.Right);
    }
}