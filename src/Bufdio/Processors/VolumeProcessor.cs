namespace Bufdio.Processors;

/// <summary>
/// A sample processor that simply multiply given audio sample to a desired volume.
/// This class cannot be inherited.
/// <para>Implements: <see cref="SampleProcessorBase"/>.</para>
/// </summary>
public sealed class VolumeProcessor : SampleProcessorBase
{
    /// <summary>
    /// Initializes <see cref="VolumeProcessor"/>. The volume range should between 0f to 1f.
    /// </summary>
    /// <param name="initialVolume">Inital desired audio volume.</param>
    public VolumeProcessor(float initialVolume = 1.0f)
    {
        Volume = initialVolume;
    }

    /// <summary>
    /// Gets or sets desired volume.
    /// </summary>
    public float Volume { get; set; }

    /// <inheritdoc />
    public override float Process(float sample)
    {
        return sample * Volume;
    }
}
