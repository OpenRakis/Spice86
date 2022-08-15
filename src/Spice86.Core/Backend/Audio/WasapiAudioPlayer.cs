namespace Spice86.Core.Backend.Audio;
using System.Runtime.Versioning;

using Spice86.Core.Backend.Audio.Wasapi;

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioPlayer : AudioPlayer {
    private readonly AudioClient audioClient;
    private readonly ManualResetEvent bufferReady = new(false);
    private RegisteredWaitHandle? callbackWaitHandle;
    private bool disposed;

    private WasapiAudioPlayer(AudioClient audioClient)
        : base(audioClient.MixFormat) {
        this.audioClient = audioClient;
    }

    public static WasapiAudioPlayer Create(TimeSpan bufferLength, bool useCallback = false) {
        AudioClient? client = MediaDevice.Default.CreateAudioClient();
        try {
            client.Initialize(bufferLength, useCallback: useCallback);
            return new WasapiAudioPlayer(client);
        } catch {
            client.Dispose();
            throw;
        }
    }

    protected override void Start(bool useCallback) {
        if (useCallback) {
            audioClient.SetEventHandle(bufferReady.SafeWaitHandle);
            callbackWaitHandle = ThreadPool.UnsafeRegisterWaitForSingleObject(bufferReady, HandleBufferReady, null, -1, false);
        }

        audioClient.Start();
    }
    protected override void Stop() {
        audioClient.Stop();
        callbackWaitHandle?.Unregister(bufferReady);
        callbackWaitHandle = null;
    }

    protected override int WriteDataInternal(Span<byte> data) {
        int written = 0;
        uint maxFrames = audioClient.GetBufferSize() - audioClient.GetCurrentPadding();
        if (maxFrames > 0) {
            bool release = audioClient.TryGetBuffer(maxFrames, out Span<byte> buffer);
            try {
                if (release) {
                    int len = Math.Min(data.Length, buffer.Length);
                    data[..len].CopyTo(buffer);
                    written = len;
                }
            } finally {
                if (release) {
                    audioClient.ReleaseBuffer((uint)written / (uint)audioClient.MixFormat.BytesPerFrame);
                }
            }
        }

        return written;
    }

    protected override void Dispose(bool disposing) {
        if (!disposed) {
            if (disposing) {
                callbackWaitHandle?.Unregister(bufferReady);
                audioClient.Dispose();
                bufferReady.Dispose();
            }

            disposed = true;
        }
    }

    private void HandleBufferReady(object? state, bool timedOut) {
        lock (audioClient) {
            uint maxFrames = audioClient.GetBufferSize() - audioClient.GetCurrentPadding();
            if (maxFrames > 0) {
                int written = 0;
                bool release = false;
                try {
                    SampleFormat format = Format.SampleFormat;
                    if (format == SampleFormat.IeeeFloat32) {
                        if (audioClient.TryGetBuffer(maxFrames, out Span<float> buffer)) {
                            release = true;
                            RaiseCallback(buffer, out written);
                        }
                    } else if (format == SampleFormat.SignedPcm16) {
                        if (audioClient.TryGetBuffer(maxFrames, out Span<short> buffer)) {
                            release = true;
                            RaiseCallback(buffer, out written);
                        }
                    } else {
                        throw new InvalidOperationException("Format not supported.");
                    }
                } finally {
                    if (release) {
                        audioClient.ReleaseBuffer((uint)written / (uint)audioClient.MixFormat.Channels);
                    }
                }
            }

            bufferReady.Reset();
        }
    }
}

