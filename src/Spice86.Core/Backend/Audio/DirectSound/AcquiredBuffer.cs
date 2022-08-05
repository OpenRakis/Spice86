namespace Spice86.Core.Backend.Audio.OpenAl.DirectSound;

using System;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal readonly struct AcquiredBuffer {
    private readonly DirectSoundBuffer buffer;
    private readonly uint length1;
    private readonly uint length2;

    public AcquiredBuffer(DirectSoundBuffer buffer, IntPtr ptr1, IntPtr ptr2, uint length1, uint length2) {
        this.buffer = buffer;
        Ptr1 = ptr1;
        Ptr2 = ptr2;
        this.length1 = length1;
        this.length2 = length2;
    }

    public bool Valid => buffer != null;
    public bool Split => Ptr2 != IntPtr.Zero;
    public IntPtr Ptr1 { get; }
    public IntPtr Ptr2 { get; }

    public Span<TSample> GetSpan1<TSample>() where TSample : unmanaged {
        unsafe {
            return new(Ptr1.ToPointer(), (int)length1 / sizeof(TSample));
        }
    }
    public Span<TSample> GetSpan2<TSample>() where TSample : unmanaged {
        if (Ptr2 == default)
            return default;

        unsafe {
            return new(Ptr2.ToPointer(), (int)length2 / sizeof(TSample));
        }
    }
}
