namespace TinyAudio;

using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;

using TinyAudio.DirectSound;
using TinyAudio.DirectSound.Interop;

[SupportedOSPlatform("windows")]
public sealed class DirectSoundAudioPlayer : AudioPlayer
{
    private readonly DirectSoundBuffer _directSoundBuffer;
    private Timer? _bufferTimer;
    private readonly uint _dataInterval;
    private volatile bool _handlingTimer;
    private bool _disposed;

    public DirectSoundAudioPlayer(AudioFormat format, TimeSpan bufferLength)
        : base(format)
    {
        IntPtr hwnd;
        using (var p = Process.GetCurrentProcess())
        {
            hwnd = p.MainWindowHandle;
        }

        if (hwnd == IntPtr.Zero)
            hwnd = NativeMethods.GetConsoleWindow();

        this._dataInterval = (uint)(bufferLength.TotalMilliseconds * 0.4);

        var dsound = DirectSoundObject.GetInstance(hwnd);
        this._directSoundBuffer = dsound.CreateBuffer(format, bufferLength);
    }

    protected override void Start(bool useCallback)
    {
        if (useCallback)
        {
            uint maxBytes = this._directSoundBuffer.GetFreeBytes();
            if (maxBytes >= 32)
                this.WriteBuffer(maxBytes);
        }

        this._directSoundBuffer.Play(PlaybackMode.LoopContinuously);

        if (useCallback)
            this._bufferTimer = new Timer(_ => this.PollingThread(), null, 0, this._dataInterval);
    }
    protected override void Stop()
    {
        this._directSoundBuffer.Stop();
        this._bufferTimer?.Dispose();
        this._bufferTimer = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (!this._disposed)
        {
            if (disposing)
            {
                this.StopPlayback();
                this._directSoundBuffer.Dispose();
            }

            this._disposed = true;
        }
    }

    private void PollingThread()
    {
        if (this._handlingTimer)
            return;

        this._handlingTimer = true;
        try
        {
            uint maxBytes = this._directSoundBuffer.GetFreeBytes() & ~3u;
            if (maxBytes >= 32)
                this.WriteBuffer(maxBytes);
        }
        finally
        {
            this._handlingTimer = false;
        }
    }

    private void WriteBuffer(uint maxBytes)
    {
        AcquiredBuffer buffer = this._directSoundBuffer.Acquire(maxBytes);
        if (buffer.Valid)
        {
            int ptr1Written = 0;
            int ptr2Written = 0;
            SampleFormat format = this.Format.SampleFormat;

            try
            {
                if (format == SampleFormat.SignedPcm16)
                {
                    Span<short> s1 = buffer.GetSpan1<short>();
                    this.RaiseCallback(s1, out ptr1Written);
                    if (buffer.Split && ptr1Written == s1.Length)
                        this.RaiseCallback(buffer.GetSpan2<short>(), out ptr2Written);
                    ptr1Written *= 2;
                    ptr2Written *= 2;
                }
                else if (format == SampleFormat.UnsignedPcm8)
                {
                    Span<byte> s1 = buffer.GetSpan1<byte>();
                    this.RaiseCallback(s1, out ptr1Written);
                    if (buffer.Split && ptr1Written == s1.Length)
                        this.RaiseCallback(buffer.GetSpan2<byte>(), out ptr2Written);
                }
                else if (format == SampleFormat.IeeeFloat32)
                {
                    Span<float> s1 = buffer.GetSpan1<float>();
                    this.RaiseCallback(s1, out ptr1Written);
                    if (buffer.Split && ptr1Written == s1.Length)
                        this.RaiseCallback(buffer.GetSpan2<float>(), out ptr2Written);
                    ptr1Written *= 4;
                    ptr2Written *= 4;
                }
                else
                {
                    throw new InvalidOperationException("Sample format not supported.");
                }
            }
            finally
            {
                this._directSoundBuffer.Unlock(buffer.Ptr1, ptr1Written, buffer.Ptr2, ptr2Written);
            }
        }
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data)
    {
        AcquiredBuffer buffer = this._directSoundBuffer.Acquire(32);
        if (buffer.Valid)
        {
            int ptr1Written = 0;
            int ptr2Written = 0;
            try
            {
                Span<byte> span1 = buffer.GetSpan1<byte>();
                ReadOnlySpan<byte> src = data[..Math.Min(span1.Length, data.Length)];
                src.CopyTo(span1);
                ptr1Written = src.Length;

                src = data[src.Length..];
                if (!src.IsEmpty && buffer.Split)
                {
                    Span<byte> span2 = buffer.GetSpan2<byte>();
                    ReadOnlySpan<byte> src2 = src.Slice(0, Math.Min(src.Length, span2.Length));
                    src2.CopyTo(span2);
                    ptr2Written = src2.Length;
                }
            }
            finally
            {
                this._directSoundBuffer.Unlock(buffer.Ptr1, ptr1Written, buffer.Ptr2, ptr2Written);
            }

            return ptr1Written + ptr2Written;
        }

        return 0;
    }
}
