namespace Spice86.Shared.Emulator.Audio;

/// <summary>
/// Represents a single audio frame.
/// </summary>
public struct AudioFrame<T> where T : struct {
    private T[] _data;

    /// <summary>
    /// Initializes a new instance of <see cref="AudioFrame{T}"/>.
    /// </summary>
    /// <param name="left">The data for the left channel.</param>
    /// <param name="right">The data for the right channel.</param>
    public AudioFrame(T left, T right) {
        _data = new T[2];
        _data[0] = left;
        _data[1] = right;
    }

    /// <summary>
    /// Represents the left audio channel.
    /// </summary>
    public T Left { get => _data[0]; set => _data[0] = value; }

    /// <summary>
    /// Represents the right audio channel.
    /// </summary>
    public T Right { get => _data[1]; set => _data[1] = value; }

    /// <summary>
    /// Returns the underlying array as a span.
    /// </summary>
    public Span<T> AsSpan() => _data.AsSpan();

    /// <inheritdoc/>
    public override string ToString() => $"Left: {Left}, Right: {Right}";

    /// <summary>
    /// The length of the audio frame.
    /// </summary>
    public int Length => _data.Length;
}