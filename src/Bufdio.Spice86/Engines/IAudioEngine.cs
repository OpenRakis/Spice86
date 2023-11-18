namespace Bufdio.Spice86.Engines;

using System;

/// <summary>
/// An interface to interact with the output audio device.
/// <para>Implements: <see cref="IDisposable"/>.</para>
/// </summary>
public interface IAudioEngine : IDisposable
{
    /// <summary>
    /// Sends audio samples to the output device.
    /// </summary>
    /// <param name="samples">Audio samples in <c>Float32</c> format.</param>
    void Send(Span<float> samples);
}
