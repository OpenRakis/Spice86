namespace Bufdio.Processors;

/// <summary>
/// Provides basic implementation of <see cref="ISampleProcessor"/> interface.
/// <para>Implements: <see cref="ISampleProcessor"/>.</para>
/// </summary>
public abstract class SampleProcessorBase : ISampleProcessor
{
    /// <summary>
    /// Initializes <see cref="SampleProcessorBase"/> object.
    /// </summary>
    protected SampleProcessorBase()
    {
        IsEnabled = true;
    }

    /// <summary>
    /// Gets or sets whether or not the sample processor is currently enabled.
    /// Intended for external use and does not affects <see cref="Process"/> method.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <inheritdoc />
    public abstract float Process(float sample);
}
