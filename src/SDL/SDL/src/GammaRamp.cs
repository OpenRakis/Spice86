using System;
using System.Text;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class GammaRamp
    {
        public GammaRampChannel? R { get; set; }
        public GammaRampChannel? G { get; set; }
        public GammaRampChannel? B { get; set; }

        public GammaRamp() { }
        public GammaRamp(float gamma)
        {
            R = new GammaRampChannel(gamma);
            G = R;
            B = R;
        }
        public GammaRamp(GammaRampChannel? r, GammaRampChannel? g, GammaRampChannel? b)
        {
            R = r;
            G = g;
            B = b;
        }

        public void Deconstruct(out GammaRampChannel? r, out GammaRampChannel? g, out GammaRampChannel? b)
        {
            r = R;
            g = G;
            b = B;
        }
    }

    public class GammaRampChannel : IReadOnlyList<ushort>
    {
        const int SIZE = 256;

        ushort[] buffer;

        public GammaRampChannel(ushort[] buffer)
        {
            if (buffer.Length != SIZE)
                throw new ArgumentException($"The buffer must be {SIZE} elements", nameof(buffer));
            this.buffer = buffer;
        }

        public GammaRampChannel() : this(new ushort[256]) { }
        public unsafe GammaRampChannel(float gamma) : this()
        {
            fixed (ushort* ptr = buffer)
                SDL_CalculateGammaRamp(gamma, ptr);
        }

        public int Count => SIZE;

        public ushort this[int index]
        {
            get { return buffer[index]; }
            set { buffer[index] = value; }
        }

        public IEnumerator<ushort> GetEnumerator()
          => (buffer as IEnumerable<ushort>).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
          => GetEnumerator();

        public Span<ushort> AsSpan()
          => buffer.AsSpan();

        public static implicit operator ushort[](GammaRampChannel ramp)
          => ramp.buffer;

        public static implicit operator Span<ushort>(GammaRampChannel ramp)
          => ramp.buffer;

        public static explicit operator GammaRampChannel(ushort[] arr)
          => new GammaRampChannel(arr);
    }
}
