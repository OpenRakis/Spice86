namespace TinyAudio;

using System;
using System.Runtime.Versioning;
using System.Threading;

using TinyAudio.Wasapi;

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioPlayer : AudioPlayer
{
    private readonly AudioClient audioClient;
    private readonly ManualResetEvent bufferReady = new(false);
    private RegisteredWaitHandle? callbackWaitHandle;
    private bool disposed;

    private WasapiAudioPlayer(AudioClient audioClient)
        : base(audioClient.MixFormat)
    {
        this.audioClient = audioClient;
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
            this.audioClient.SetEventHandle(this.bufferReady.SafeWaitHandle);
            this.callbackWaitHandle = ThreadPool.UnsafeRegisterWaitForSingleObject(this.bufferReady, this.HandleBufferReady, null, -1, false);
        }

        this.audioClient.Start();
    }
    protected override void Stop()
    {
        this.audioClient.Stop();
        this.callbackWaitHandle?.Unregister(this.bufferReady);
        this.callbackWaitHandle = null;
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data)
    {
        int written = 0;
        uint maxFrames = this.audioClient.GetBufferSize() - this.audioClient.GetCurrentPadding();
        if (maxFrames > 0)
        {
            bool release = this.audioClient.TryGetBuffer<byte>(maxFrames, out Span<byte> buffer);
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
                    this.audioClient.ReleaseBuffer((uint)written / (uint)this.audioClient.MixFormat.BytesPerFrame);
            }
        }

        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                this.callbackWaitHandle?.Unregister(this.bufferReady);
                this.audioClient.Dispose();
                this.bufferReady.Dispose();
            }

            this.disposed = true;
        }
    }

    private void HandleBufferReady(object? state, bool timedOut)
    {
        lock (this.audioClient)
        {
            uint maxFrames = this.audioClient.GetBufferSize() - this.audioClient.GetCurrentPadding();
            if (maxFrames > 0)
            {
                int written = 0;
                bool release = false;
                try
                {
                    SampleFormat format = this.Format.SampleFormat;
                    if (format == SampleFormat.IeeeFloat32)
                    {
                        if (this.audioClient.TryGetBuffer<float>(maxFrames, out Span<float> buffer))
                        {
                            release = true;
                            this.RaiseCallback(buffer, out written);
                        }
                    }
                    else if (format == SampleFormat.SignedPcm16)
                    {
                        if (this.audioClient.TryGetBuffer<short>(maxFrames, out Span<short> buffer))
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
                        this.audioClient.ReleaseBuffer((uint)written / (uint)this.audioClient.MixFormat.Channels);
                }
            }

            this.bufferReady.Reset();
        }
    }
}
