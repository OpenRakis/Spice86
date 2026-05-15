namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>
/// <see cref="IDataSource"/> adapter that exposes a contiguous byte window
/// inside a larger backing source. Used to expose the PCM payload of a
/// WAV file as if it were the entire data area of an audio track.
/// </summary>
public sealed class WindowedDataSource : IDataSource
{
    private readonly IDataSource _inner;
    private readonly long _windowStart;

    /// <summary>Creates a window over <paramref name="inner"/>.</summary>
    /// <param name="inner">The backing source whose bytes are being windowed.</param>
    /// <param name="windowStart">Absolute byte offset of the window start within <paramref name="inner"/>.</param>
    /// <param name="windowLength">Number of bytes accessible through the window.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when offsets are negative or extend past <paramref name="inner"/>.</exception>
    public WindowedDataSource(IDataSource inner, long windowStart, long windowLength)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (windowStart < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowStart), windowStart, "Window start must be non-negative.");
        }
        if (windowLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowLength), windowLength, "Window length must be non-negative.");
        }
        if (windowStart + windowLength > inner.LengthBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(windowLength), windowLength, "Window extends past the inner source length.");
        }
        _inner = inner;
        _windowStart = windowStart;
        LengthBytes = windowLength;
    }

    /// <inheritdoc/>
    public long LengthBytes { get; }

    /// <inheritdoc/>
    public int Read(long byteOffset, Span<byte> destination)
    {
        if (byteOffset < 0 || byteOffset >= LengthBytes)
        {
            return 0;
        }
        int available = (int)Math.Min(destination.Length, LengthBytes - byteOffset);
        if (available <= 0)
        {
            return 0;
        }
        return _inner.Read(_windowStart + byteOffset, destination.Slice(0, available));
    }
}
