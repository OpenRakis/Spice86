namespace TinyAudio.DirectSound;
using System;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal readonly struct AcquiredBuffer
{
    private readonly DirectSoundBuffer _buffer;
    private readonly uint _length1;
    private readonly uint _length2;

    public AcquiredBuffer(DirectSoundBuffer buffer, IntPtr ptr1, IntPtr ptr2, uint length1, uint length2)
    {
        this._buffer = buffer;
        this.Ptr1 = ptr1;
        this.Ptr2 = ptr2;
        this._length1 = length1;
        this._length2 = length2;
    }

    public bool Valid => this._buffer != null;
    public bool Split => this.Ptr2 != IntPtr.Zero;
    public IntPtr Ptr1 { get; }
    public IntPtr Ptr2 { get; }

    public Span<TSample> GetSpan1<TSample>() where TSample : unmanaged
    {
        unsafe
        {
            return new(this.Ptr1.ToPointer(), (int)this._length1 / sizeof(TSample));
        }
    }
    public Span<TSample> GetSpan2<TSample>() where TSample : unmanaged
    {
        if (this.Ptr2 == default)
            return default;

        unsafe
        {
            return new(this.Ptr2.ToPointer(), (int)this._length2 / sizeof(TSample));
        }
    }
}