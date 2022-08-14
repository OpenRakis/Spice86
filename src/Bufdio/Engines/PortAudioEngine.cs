using System;
using System.Runtime.InteropServices;
using Bufdio.Bindings.PortAudio;
using Bufdio.Exceptions;
using Bufdio.Utilities.Extensions;

namespace Bufdio.Engines;

/// <summary>
/// Interact with output audio device by using PortAudio library.
/// This class cannot be inherited.
/// <para>Implements: <see cref="IAudioEngine"/>.</para>
/// </summary>
public sealed class PortAudioEngine : IAudioEngine
{
    private const int FramesPerBuffer = 0; // paFramesPerBufferUnspecified
    private const PaBinding.PaStreamFlags StreamFlags = PaBinding.PaStreamFlags.paNoFlag;
    private readonly AudioEngineOptions _options;
    private readonly IntPtr _stream;
    private bool _disposed;

    /// <summary>
    /// Initializes <see cref="PortAudioEngine"/> object.
    /// </summary>
    /// <param name="options">Optional audio engine options.</param>
    /// <exception cref="PortAudioException">
    /// Might be thrown when errors occured during PortAudio stream initialization.
    /// </exception>
    public PortAudioEngine(AudioEngineOptions options = default)
    {
        _options = options ?? new AudioEngineOptions();

        var parameters = new PaBinding.PaStreamParameters
        {
            channelCount = _options.Channels,
            device = _options.Device.DeviceIndex,
            hostApiSpecificStreamInfo = IntPtr.Zero,
            sampleFormat = BufdioLib.Constants.PaSampleFormat,
            suggestedLatency = _options.Latency
        };

        IntPtr stream;

        unsafe
        {
            PaBinding.PaStreamParameters tempParameters;
            var parametersPtr = new IntPtr(&tempParameters);
            Marshal.StructureToPtr(parameters, parametersPtr, false);

            var code = PaBinding.Pa_OpenStream(
                new IntPtr(&stream),
                IntPtr.Zero,
                parametersPtr,
                _options.SampleRate,
                FramesPerBuffer,
                StreamFlags,
                null,
                IntPtr.Zero);

            code.PaGuard();
        }

        _stream = stream;

        PaBinding.Pa_StartStream(_stream).PaGuard();
    }

    /// <inheritdoc />
    public void Send(Span<float> samples)
    {
        unsafe
        {
            fixed (float* buffer = samples)
            {
                var frames = samples.Length / _options.Channels;
                PaBinding.Pa_WriteStream(_stream, (IntPtr)buffer, frames);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed || _stream == IntPtr.Zero)
        {
            return;
        }

        PaBinding.Pa_AbortStream(_stream);
        PaBinding.Pa_CloseStream(_stream);

        _disposed = true;
    }
}
