
using Bufdio.Spice86.Bindings.PortAudio;
using Bufdio.Spice86.Bindings.PortAudio.Enums;
using Bufdio.Spice86.Bindings.PortAudio.Structs;
using Bufdio.Spice86.Exceptions;
using Bufdio.Spice86.Utilities.Extensions;

using System;
using System.Runtime.InteropServices;

namespace Bufdio.Spice86.Engines;

/// <summary>
/// Interact with output audio device by using the PortAudio library.
/// This class cannot be inherited.
/// <para>Implements: <see cref="IAudioEngine"/>.</para>
/// </summary>
public sealed class PortAudioEngine : IAudioEngine {
    private const PaStreamFlags StreamFlags = PaStreamFlags.paNoFlag;
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

        PaStreamParameters parameters = new() {
            ChannelCount = _options.Channels,
            Device = _options.DefaultAudioDevice.DeviceIndex,
            HostApiSpecificStreamInfo = IntPtr.Zero,
            SampleFormat = (PaSampleFormat)PortAudioLib.Constants.PaSampleFormat,
            SuggestedLatency = _options.Latency
        };

        IntPtr stream;

        unsafe {
            PaStreamParameters tempParameters;
            IntPtr parametersPtr = new(&tempParameters);
            Marshal.StructureToPtr(parameters, parametersPtr, false);
            int code = NativeMethods.PortAudioOpenStream(new IntPtr(&stream), IntPtr.Zero, parametersPtr, _options.SampleRate, framesPerBuffer, StreamFlags, null, IntPtr.Zero);
            code.PaGuard();
        }
        _stream = stream;

        NativeMethods.PortAudioStartStream(_stream).PaGuard();
    }

    /// <inheritdoc />
    public unsafe void Send(Span<float> frames) {
        fixed (float* buffer = frames) {
            NativeMethods.PortAudioWriteStream(_stream, (IntPtr)buffer, frames.Length / _options.Channels);
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
                NativeMethods.PortAudioAbortStream(_stream);
                NativeMethods.PortAudioCloseStream(_stream);
            }
            _disposed = true;
        }
    }
}
