namespace Spice86.Core.Backend.Audio;
using System.Diagnostics;
using System.Runtime.Versioning;

using Spice86.Core.Backend.Audio.OpenAl.DirectSound;
using Spice86.Core.Backend.Audio.OpenAl.DirectSound.Interop;

[SupportedOSPlatform("windows")]
public sealed class DirectSoundAudioPlayer : AudioPlayer {
    private readonly DirectSoundBuffer directSoundBuffer;
    private Timer? bufferTimer;
    private readonly uint dataInterval;
    private volatile bool handlingTimer;
    private bool disposed;

    public DirectSoundAudioPlayer(AudioFormat format, TimeSpan bufferLength)
        : base(format) {
        IntPtr hwnd;
        using (var p = Process.GetCurrentProcess()) {
            hwnd = p.MainWindowHandle;
        }

        if (hwnd == IntPtr.Zero)
            hwnd = NativeMethods.GetConsoleWindow();

        dataInterval = (uint)(bufferLength.TotalMilliseconds * 0.4);

        var dsound = DirectSoundObject.GetInstance(hwnd);
        directSoundBuffer = dsound.CreateBuffer(format, bufferLength);
    }

    protected override void Start(bool useCallback) {
        if (useCallback) {
            uint maxBytes = directSoundBuffer.GetFreeBytes();
            if (maxBytes >= 32)
                WriteBuffer(maxBytes);
        }

        directSoundBuffer.Play(PlaybackMode.LoopContinuously);

        if (useCallback)
            bufferTimer = new Timer(_ => PollingThread(), null, 0, dataInterval);
    }
    protected override void Stop() {
        directSoundBuffer.Stop();
        bufferTimer?.Dispose();
        bufferTimer = null;
    }

    protected override void Dispose(bool disposing) {
        if (!disposed) {
            if (disposing) {
                StopPlayback();
                directSoundBuffer.Dispose();
            }

            disposed = true;
        }
    }

    private void PollingThread() {
        if (handlingTimer)
            return;

        handlingTimer = true;
        try {
            uint maxBytes = directSoundBuffer.GetFreeBytes() & ~3u;
            if (maxBytes >= 32)
                WriteBuffer(maxBytes);
        } finally {
            handlingTimer = false;
        }
    }

    private void WriteBuffer(uint maxBytes) {
        AcquiredBuffer buffer = directSoundBuffer.Acquire(maxBytes);
        if (buffer.Valid) {
            int ptr1Written = 0;
            int ptr2Written = 0;
            SampleFormat format = Format.SampleFormat;

            try {
                if (format == SampleFormat.SignedPcm16) {
                    Span<short> s1 = buffer.GetSpan1<short>();
                    RaiseCallback(s1, out ptr1Written);
                    if (buffer.Split && ptr1Written == s1.Length)
                        RaiseCallback(buffer.GetSpan2<short>(), out ptr2Written);
                    ptr1Written *= 2;
                    ptr2Written *= 2;
                } else if (format == SampleFormat.UnsignedPcm8) {
                    Span<byte> s1 = buffer.GetSpan1<byte>();
                    RaiseCallback(s1, out ptr1Written);
                    if (buffer.Split && ptr1Written == s1.Length)
                        RaiseCallback(buffer.GetSpan2<byte>(), out ptr2Written);
                } else if (format == SampleFormat.IeeeFloat32) {
                    Span<float> s1 = buffer.GetSpan1<float>();
                    RaiseCallback(s1, out ptr1Written);
                    if (buffer.Split && ptr1Written == s1.Length)
                        RaiseCallback(buffer.GetSpan2<float>(), out ptr2Written);
                    ptr1Written *= 4;
                    ptr2Written *= 4;
                } else {
                    throw new InvalidOperationException("Sample format not supported.");
                }
            } finally {
                directSoundBuffer.Unlock(buffer.Ptr1, ptr1Written, buffer.Ptr2, ptr2Written);
            }
        }
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data) {
        AcquiredBuffer buffer = directSoundBuffer.Acquire(32);
        if (buffer.Valid) {
            int ptr1Written = 0;
            int ptr2Written = 0;
            try {
                Span<byte> span1 = buffer.GetSpan1<byte>();
                ReadOnlySpan<byte> src = data[..Math.Min(span1.Length, data.Length)];
                src.CopyTo(span1);
                ptr1Written = src.Length;

                src = data[src.Length..];
                if (!src.IsEmpty && buffer.Split) {
                    Span<byte> span2 = buffer.GetSpan2<byte>();
                    ReadOnlySpan<byte> src2 = src[..Math.Min(src.Length, span2.Length)];
                    src2.CopyTo(span2);
                    ptr2Written = src2.Length;
                }
            } finally {
                directSoundBuffer.Unlock(buffer.Ptr1, ptr1Written, buffer.Ptr2, ptr2Written);
            }

            return ptr1Written + ptr2Written;
        }

        return 0;
    }
}
