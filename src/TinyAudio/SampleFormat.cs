namespace TinyAudio;

using System.Runtime.Versioning;

/// <summary>
/// Used to specify the number of bits per sample in a sound buffer.
/// </summary>
public enum SampleFormat
{
    /// <summary>
    /// There are eight bits per sample.
    /// </summary>
    UnsignedPcm8,
    /// <summary>
    /// There are sixteen bits per sample.
    /// </summary>
    SignedPcm16,
    /// <summary>
    /// Samples are 32-bit IEEE floating point values.
    /// </summary>
    IeeeFloat32
}
