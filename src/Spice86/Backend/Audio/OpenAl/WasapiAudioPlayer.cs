namespace Spice86.Backend.Audio.OpenAl;
using System.Runtime.Versioning;

using Spice86.Backend.Audio.OpenAl.Wasapi;

[SupportedOSPlatform(("windows"))]
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
        var client = MediaDevice.Default.CreateAudioClient();
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
            bool release = this.audioClient.TryGetBuffer<byte>(maxFrames, out var buffer);
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
                    var format = this.Format.SampleFormat;
                    if (format == SampleFormat.IeeeFloat32)
                    {
                        if (this.audioClient.TryGetBuffer<float>(maxFrames, out var buffer))
                        {
                            release = true;
                            this.RaiseCallback(buffer, out written);
                        }
                    }
                    else if (format == SampleFormat.SignedPcm16)
                    {
                        if (this.audioClient.TryGetBuffer<short>(maxFrames, out var buffer))
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

