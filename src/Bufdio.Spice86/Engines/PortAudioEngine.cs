namespace Bufdio.Spice86.Engines;
using System;
using System.Runtime.InteropServices;

using Bufdio.Spice86.Bindings.PortAudio;
using Bufdio.Spice86.Exceptions;
using Bufdio.Spice86.Utilities;
using Bufdio.Spice86.Utilities.Extensions;

/// <summary>
/// Interact with output audio device by using PortAudio library.
/// This class cannot be inherited.
/// <para>Implements: <see cref="IAudioEngine"/>.</para>
/// </summary>
public sealed class PortAudioEngine : IAudioEngine {
    private const PaBinding.PaStreamFlags StreamFlags = PaBinding.PaStreamFlags.paNoFlag;
    private readonly AudioEngineOptions _options;
    private readonly IntPtr _stream;
    private bool _disposed;

    /// <summary>
    /// Initializes <see cref="PortAudioEngine"/> object.
    /// </summary>
    /// <param name="framesPerBuffer">Must be a power of 2. Can be 0 for undefined.</param>
    /// <param name="options">Optional audio engine options.</param>
    /// <exception cref="PortAudioException">
    /// Might be thrown when errors occured during PortAudio stream initialization.
    /// </exception>
    public PortAudioEngine(int framesPerBuffer, AudioEngineOptions? options = default) {
        _options = options ?? new AudioEngineOptions();

        PaBinding.PaStreamParameters parameters = new PaBinding.PaStreamParameters {
            channelCount = _options.Channels,
            device = _options.Device.DeviceIndex,
            hostApiSpecificStreamInfo = IntPtr.Zero,
            sampleFormat = BufdioLib.Constants.PaSampleFormat,
            suggestedLatency = _options.Latency
        };

        IntPtr stream;

        unsafe {
            PaBinding.PaStreamParameters tempParameters;
            IntPtr parametersPtr = new(&tempParameters);
            Marshal.StructureToPtr(parameters, parametersPtr, false);

            int code = 0;
            if (PlatformInfo.IsWindows) {
                code = PaBinding.Windows.Pa_OpenStream(new IntPtr(&stream), IntPtr.Zero, parametersPtr, _options.SampleRate, framesPerBuffer, StreamFlags, null, IntPtr.Zero);
                _stream = stream;
            } else if (PlatformInfo.IsLinux) {
                code = PaBinding.Linux.Pa_OpenStream(new IntPtr(&stream), IntPtr.Zero, parametersPtr, _options.SampleRate, framesPerBuffer, StreamFlags, null, IntPtr.Zero);
                _stream = stream;
            } else if (PlatformInfo.IsOSX) {
                code  = PaBinding.OSX.Pa_OpenStream(new IntPtr(&stream), IntPtr.Zero, parametersPtr, _options.SampleRate, framesPerBuffer, StreamFlags, null, IntPtr.Zero);
                _stream = stream;
            }

            code.PaGuard();
        }

        if (PlatformInfo.IsWindows) {
            PaBinding.Windows.Pa_StartStream(_stream).PaGuard();
        } else if (PlatformInfo.IsLinux) {
            PaBinding.Linux.Pa_StartStream(_stream).PaGuard();
        } else if (PlatformInfo.IsOSX) {
            PaBinding.OSX.Pa_StartStream(_stream).PaGuard();
        }
    }

    /// <inheritdoc />
    public void Send(Span<float> samples) {
        unsafe {
            fixed (float* buffer = samples) {
                int frames = samples.Length / _options.Channels;
                
                if (PlatformInfo.IsWindows) {
                    PaBinding.Windows.Pa_WriteStream(_stream, (IntPtr)buffer, frames);
                } else if (PlatformInfo.IsLinux) {
                    PaBinding.Linux.Pa_WriteStream(_stream, (IntPtr)buffer, frames);
                } else if (PlatformInfo.IsOSX) {
                    PaBinding.OSX.Pa_WriteStream(_stream, (IntPtr)buffer, frames);
                }
            }
        }
    }

    /// <summary>
    /// Releases the native library (PortAudio)
    /// </summary>
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                if (PlatformInfo.IsWindows) {
                    PaBinding.Windows.Pa_AbortStream(_stream);
                    PaBinding.Windows.Pa_CloseStream(_stream);
                } else if (PlatformInfo.IsLinux) {
                    PaBinding.Linux.Pa_AbortStream(_stream);
                    PaBinding.Linux.Pa_CloseStream(_stream);
                } else if (PlatformInfo.IsOSX) {
                    PaBinding.OSX.Pa_AbortStream(_stream);
                    PaBinding.OSX.Pa_CloseStream(_stream);
                }
            }
            _disposed = true;
        }
    }
}
