namespace TinyAudio;

using System;
using System.Runtime.Versioning;
using System.Threading;

using TinyAudio.Wasapi;

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioPlayer : AudioPlayer
{
    private readonly AudioClient _audioClient;
    private readonly ManualResetEvent _bufferReady = new(false);
    private RegisteredWaitHandle? _callbackWaitHandle;
    private bool _disposed;

    private WasapiAudioPlayer(AudioClient audioClient)
        : base(audioClient.MixFormat)
    {
        this._audioClient = audioClient;
    }

    public static WasapiAudioPlayer Create(TimeSpan bufferLength, bool useCallback = false)
    {
        AudioClient? client = MediaDevice.Default.CreateAudioClient();
        try
        {
            client.Initialize(bufferLength, useCallback: useCallback);
            return new WasapiAudioPlayer(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    protected override void Start(bool useCallback)
    {
        if (useCallback)
        {
            this._audioClient.SetEventHandle(this._bufferReady.SafeWaitHandle);
            this._callbackWaitHandle = ThreadPool.UnsafeRegisterWaitForSingleObject(this._bufferReady, this.HandleBufferReady, null, -1, false);
        }

        this._audioClient.Start();
    }
    protected override void Stop()
    {
        this._audioClient.Stop();
        this._callbackWaitHandle?.Unregister(this._bufferReady);
        this._callbackWaitHandle = null;
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data)
    {
        int written = 0;
        uint maxFrames = this._audioClient.GetBufferSize() - this._audioClient.GetCurrentPadding();
        if (maxFrames > 0)
        {
            bool release = this._audioClient.TryGetBuffer<byte>(maxFrames, out Span<byte> buffer);
            try
            {
                if (release)
                {
                    int len = Math.Min(data.Length, buffer.Length);
                    data[..len].CopyTo(buffer);
                    written = len;
                }
            }
            finally
            {
                if (release)
                    this._audioClient.ReleaseBuffer((uint)written / (uint)this._audioClient.MixFormat.BytesPerFrame);
            }
        }

        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (!this._disposed)
        {
            if (disposing)
            {
                this._callbackWaitHandle?.Unregister(this._bufferReady);
                this._audioClient.Dispose();
                this._bufferReady.Dispose();
            }

            this._disposed = true;
        }
    }

    private void HandleBufferReady(object? state, bool timedOut)
    {
        lock (this._audioClient)
        {
            uint maxFrames = this._audioClient.GetBufferSize() - this._audioClient.GetCurrentPadding();
            if (maxFrames > 0)
            {
                int written = 0;
                bool release = false;
                try
                {
                    SampleFormat format = this.Format.SampleFormat;
                    if (format == SampleFormat.IeeeFloat32)
                    {
                        if (this._audioClient.TryGetBuffer<float>(maxFrames, out Span<float> buffer))
                        {
                            release = true;
                            this.RaiseCallback(buffer, out written);
                        }
                    }
                    else if (format == SampleFormat.SignedPcm16)
                    {
                        if (this._audioClient.TryGetBuffer<short>(maxFrames, out Span<short> buffer))
                        {
                            release = true;
                            this.RaiseCallback(buffer, out written);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Format not supported.");
                    }
                }
                finally
                {
                    if (release)
                        this._audioClient.ReleaseBuffer((uint)written / (uint)this._audioClient.MixFormat.Channels);
                }
            }

            this._bufferReady.Reset();
        }
    }
}
