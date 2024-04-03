namespace Spice86.Shared.Emulator.Audio;

/// <summary>
/// Represents a single audio frame.
/// </summary>
public struct AudioFrame<T> where T : struct {
    private Memory<T> _data;

    /// <summary>
    /// Initializes a new instance of <see cref="AudioFrame{T}"/>.
    /// </summary>
    /// <param name="left">The data for the left channel.</param>
    /// <param name="right">The data for the right channel.</param>
    public AudioFrame(T left, T right) {
        _data = new Memory<T>([left, right]);
    }

    /// <summary>
    /// Represents the left audio channel.
    /// </summary>
    public T Left { get => _data.Span[0]; set => _data.Span[0] = value; }

    /// <summary>
    /// Represents the right audio channel.
    /// </summary>
    public T Right { get => _data.Span[1]; set => _data.Span[1] = value; }

    /// <summary>
    /// Returns the underlying memory store as a span.
    /// </summary>
    public Span<T> AsSpan() => _data.Span;

    /// <inheritdoc/>
    public override string ToString() => $"Left: {Left}, Right: {Right}";

    /// <summary>
    /// The length of the audio frame.
    /// </summary>
    public int Length => _data.Length;
    
    /// <summary>
    /// Implicitly converts the audio frame to a span of T.
    /// </summary>
    /// <param name="frame">The AudioFrame to convert.</param>
    /// <returns>The AudioFrame as a Span.</returns>
    public static implicit operator Span<T>(AudioFrame<T> frame) => frame.AsSpan();
}