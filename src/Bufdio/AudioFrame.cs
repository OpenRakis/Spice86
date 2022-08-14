namespace Bufdio;

/// <summary>
/// Represents audio frame object. Each audio frames contains their own presentation time
/// and raw of audio that can be written into output device.
/// This class cannot be inherited.
/// </summary>
public sealed class AudioFrame
{
    /// <summary>
    /// Initializes <see cref="AudioFrame"/> object.
    /// </summary>
    /// <param name="presentationTime">Presentation time of audio frame in milliseconds.</param>
    /// <param name="data">Audio samples in <c>Float32</c> format that can be written to output device.</param>
    public AudioFrame(double presentationTime, byte[] data)
    {
        PresentationTime = presentationTime;
        Data = data;
    }

    /// <summary>
    /// Gets frame presentation time in milliseconds.
    /// </summary>
    public double PresentationTime { get; }

    /// <summary>
    /// Gets audio samples in <c>Float32</c> format that can be written to output device.
    /// </summary>
    public byte[] Data { get; }
}
