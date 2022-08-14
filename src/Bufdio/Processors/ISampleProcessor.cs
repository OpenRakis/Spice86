namespace Bufdio.Processors;

/// <summary>
/// An interface that is intended to manipulate specified audio sample in <c>Float32</c> format
/// before its gets send out to the output device.
/// </summary>
public interface ISampleProcessor
{
    /// <summary>
    /// Gets or sets whether or not the sample processor is currently enabled.
    /// Intended for external use should not affects <see cref="Process"/> method.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Process or manipulate given audio sample in <c>Float32</c> format.
    /// </summary>
    /// <param name="sample">Audio sample to be processed.</param>
    /// <returns>Processed sample in <c>Float32</c> format.</returns>
    float Process(float sample);
}
