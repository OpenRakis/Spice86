namespace Bufdio.Decoders.FFmpeg;

/// <summary>
/// Options for decoding (and, or) resampling specified audio source that can be passed
/// through <see cref="FFmpegDecoder"/> class. This class cannot be inherited.
/// </summary>
public sealed class FFmpegDecoderOptions
{
    /// <summary>
    /// Initializes <see cref="FFmpegDecoderOptions"/> object.
    /// </summary>
    /// <param name="channels">Desired audio channel count.</param>
    /// <param name="sampleRate">Desired audio sample rate.</param>
    public FFmpegDecoderOptions(int channels = 2, int sampleRate = 44100)
    {
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>
    /// Gets destination audio channel count.
    /// </summary>
    public int Channels { get; }

    /// <summary>
    /// Gets destination audio sample rate.
    /// </summary>
    public int SampleRate { get; }
}
