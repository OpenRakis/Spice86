using System;

namespace Bufdio.Decoders;

/// <summary>
/// An interface for decoding audio frames from given audio source.
/// <para>Implements: <see cref="IDisposable"/>.</para>
/// </summary>
public interface IAudioDecoder : IDisposable
{
    /// <summary>
    /// Gets the information about loaded audio source.
    /// </summary>
    AudioStreamInfo StreamInfo { get; }

    /// <summary>
    /// Decode next available audio frame from loaded audio source.
    /// </summary>
    /// <returns>A new <see cref="AudioDecoderResult"/> data.</returns>
    AudioDecoderResult DecodeNextFrame();

    /// <summary>
    /// Try to seeks audio stream to the specified position and returns <c>true</c> if successfully seeks,
    /// otherwise, <c>false</c>.
    /// </summary>
    /// <param name="position">Desired seek position.</param>
    /// <param name="error">An error message while seeking audio stream.</param>
    /// <returns><c>true</c> if successfully seeks, otherwise, <c>false</c>.</returns>
    bool TrySeek(TimeSpan position, out string error);
}
