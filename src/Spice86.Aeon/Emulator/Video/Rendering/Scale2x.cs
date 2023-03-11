using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spice86.Aeon.Emulator.Video.Rendering
{
    internal sealed class Scale2x : Scaler
    {
        public Scale2x(int width, int height) : base(width, height)
        {
        }

        public override int TargetWidth => SourceWidth * 2;
        public override int TargetHeight => SourceHeight * 2;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        protected override void Scale(IntPtr source, IntPtr destination)
        {
            unsafe
            {
                int width = SourceWidth;
                int height = SourceHeight;
                var psrc = (uint*)source.ToPointer();
                var pdest = (uint*)destination.ToPointer();

                int destPitch = width * 2;

                int srcBottomRowStart = width * (height - 1);
                int destBottomRowStart = (destPitch * (height - 1)) * 2;

                for (int i = 0; i < width; i++)
                {
                    CopyToDest(psrc, pdest, i, i * 2, destPitch);
                    CopyToDest(psrc, pdest, srcBottomRowStart + i, destBottomRowStart + (i * 2), destPitch);
                }

                int srcIndex;
                int destIndex;

                for (int y = 1; y < height - 1; y++)
                {
                    srcIndex = y * width;
                    destIndex = destPitch * y * 2;

                    CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);
                    srcIndex++;
                    destIndex += 2;

                    for (int x = 1; x < width - 1; x++)
                    {
                        ExpandPixel(psrc, width, pdest, destPitch, srcIndex, destIndex);

                        srcIndex++;
                        destIndex += 2;
                    }

                    CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        protected override void VectorScale(IntPtr source, IntPtr destinaton)
        {
            unsafe
            {
                int width = SourceWidth;
                int height = SourceHeight;
                var psrc = (uint*)source.ToPointer();
                var pdest = (uint*)destinaton.ToPointer();

                int destPitch = width * 2;

                int srcBottomRowStart = width * (height - 1);
                int destBottomRowStart = (destPitch * (height - 1)) * 2;

                for (int i = 0; i < width; i++)
                {
                    CopyToDest(psrc, pdest, i, i * 2, destPitch);
                    CopyToDest(psrc, pdest, srcBottomRowStart + i, destBottomRowStart + (i * 2), destPitch);
                }

                int srcIndex;
                int destIndex;

                int rowRemainder = (width - 2) % Vector<uint>.Count;

                for (int y = 1; y < height - 1; y++)
                {
                    srcIndex = y * width;
                    destIndex = y * 2 * width * 2;

                    CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);

                    srcIndex++;
                    destIndex += 2;

                    var srcSpan = new ReadOnlySpan<uint>(psrc + srcIndex, width - 2);
                    var aSpan = new ReadOnlySpan<uint>(psrc + srcIndex - width, width - 2);
                    var bSpan = new ReadOnlySpan<uint>(psrc + srcIndex + 1, width - 2);
                    var cSpan = new ReadOnlySpan<uint>(psrc + srcIndex - 1, width - 2);
                    var dSpan = new ReadOnlySpan<uint>(psrc + srcIndex + width, width - 2);

                    var srcVec = MemoryMarshal.Cast<uint, Vector<uint>>(srcSpan);
                    var aVec = MemoryMarshal.Cast<uint, Vector<uint>>(aSpan);
                    var bVec = MemoryMarshal.Cast<uint, Vector<uint>>(bSpan);
                    var cVec = MemoryMarshal.Cast<uint, Vector<uint>>(cSpan);
                    var dVec = MemoryMarshal.Cast<uint, Vector<uint>>(dSpan);

                    for (int i = 0; i < aVec.Length; i++)
                    {
                        var o1 = Vector.ConditionalSelect(
                            Vector.AndNot(Vector.AndNot(Vector.Equals(cVec[i], aVec[i]), Vector.Equals(cVec[i], dVec[i])), Vector.Equals(aVec[i], bVec[i])),
                            aVec[i],
                            srcVec[i]
                        );
                        for (int j = 0; j < Vector<uint>.Count; j++)
                            pdest[destIndex + j * 2] = o1[j];

                        var o2 = Vector.ConditionalSelect(
                            Vector.AndNot(Vector.AndNot(Vector.Equals(aVec[i], bVec[i]), Vector.Equals(aVec[i], cVec[i])), Vector.Equals(bVec[i], dVec[i])),
                            bVec[i],
                            srcVec[i]
                        );
                        for (int j = 0; j < Vector<uint>.Count; j++)
                            pdest[destIndex + j * 2 + 1] = o2[j];

                        var o3 = Vector.ConditionalSelect(
                            Vector.AndNot(Vector.AndNot(Vector.Equals(dVec[i], cVec[i]), Vector.Equals(dVec[i], bVec[i])), Vector.Equals(cVec[i], aVec[i])),
                            cVec[i],
                            srcVec[i]
                        );
                        for (int j = 0; j < Vector<uint>.Count; j++)
                            pdest[destIndex + j * 2 + destPitch] = o3[j];

                        var o4 = Vector.ConditionalSelect(
                            Vector.AndNot(Vector.AndNot(Vector.Equals(bVec[i], dVec[i]), Vector.Equals(bVec[i], aVec[i])), Vector.Equals(dVec[i], cVec[i])),
                            dVec[i],
                            srcVec[i]
                        );
                        for (int j = 0; j < Vector<uint>.Count; j++)
                            pdest[destIndex + j * 2 + destPitch + 1] = o4[j];

                        destIndex += Vector<uint>.Count * 2;
                        srcIndex += Vector<uint>.Count;
                    }

                    for (int i = 0; i < rowRemainder; i++)
                    {
                        ExpandPixel(psrc, width, pdest, destPitch, srcIndex, destIndex);
                        srcIndex++;
                        destIndex += 2;
                    }

                    CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void ExpandPixel(uint* psrc, int width, uint* pdest, int destPitch, int srcIndex, int destIndex)
        {
            uint a = psrc[srcIndex - width];
            uint b = psrc[srcIndex + 1];
            uint c = psrc[srcIndex - 1];
            uint d = psrc[srcIndex + width];

            uint o1 = psrc[srcIndex];
            uint o2 = o1;
            uint o3 = o1;
            uint o4 = o1;

            if (c == a && c != d && a != b)
                o1 = a;

            if (a == b && a != c && b != d)
                o2 = b;

            if (d == c && d != b && c != a)
                o3 = c;

            if (b == d && b != a && d != c)
                o4 = d;

            pdest[destIndex] = o1;
            pdest[destIndex + 1] = o2;
            pdest[destIndex + destPitch] = o3;
            pdest[destIndex + destPitch + 1] = o4;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyToDest(uint* psrc, uint* pdest, int srcIndex, int destIndex, int destPitch)
        {
            uint value = psrc[srcIndex];
            pdest[destIndex] = value;
            pdest[destIndex + 1] = value;
            pdest[destIndex + destPitch] = value;
            pdest[destIndex + destPitch + 1] = value;
        }
    }
}
