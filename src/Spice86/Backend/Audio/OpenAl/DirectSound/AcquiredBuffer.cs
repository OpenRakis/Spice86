namespace Spice86.Backend.Audio.OpenAl.DirectSound;
using System;
using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
    internal readonly struct AcquiredBuffer
    {
        private readonly DirectSoundBuffer buffer;
        private readonly uint length1;
        private readonly uint length2;

        public AcquiredBuffer(DirectSoundBuffer buffer, IntPtr ptr1, IntPtr ptr2, uint length1, uint length2)
        {
            this.buffer = buffer;
            this.Ptr1 = ptr1;
            this.Ptr2 = ptr2;
            this.length1 = length1;
            this.length2 = length2;
        }

        public bool Valid => this.buffer != null;
        public bool Split => this.Ptr2 != IntPtr.Zero;
        public IntPtr Ptr1 { get; }
        public IntPtr Ptr2 { get; }

        public Span<TSample> GetSpan1<TSample>() where TSample : unmanaged
        {
            unsafe
            {
                return new(this.Ptr1.ToPointer(), (int)this.length1 / sizeof(TSample));
            }
        }
        public Span<TSample> GetSpan2<TSample>() where TSample : unmanaged
        {
            if (this.Ptr2 == default)
                return default;

            unsafe
            {
                return new(this.Ptr2.ToPointer(), (int)this.length2 / sizeof(TSample));
            }
        }
    }
