using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spice86.Aeon.Emulator.Video.Rendering; 

internal sealed class Scale3x : Scaler
{
    public Scale3x(int width, int height) : base(width, height)
    {
    }

    public override int TargetWidth => SourceWidth * 3;
    public override int TargetHeight => SourceHeight * 3;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Scale(IntPtr source, IntPtr destination)
    {
        unsafe
        {
            uint* psrc = (uint*)source.ToPointer();
            uint* pdest = (uint*)destination.ToPointer();
            int width = SourceWidth;
            int height = SourceHeight;

            int destPitch = width * 3;

            int srcBottomRowStart = width * (height - 1);
            int destBottomRowStart = (destPitch * (height - 1)) * 3;

            for (int i = 0; i < width; i++)
            {
                CopyToDest(psrc, pdest, i, i * 3, destPitch);
                CopyToDest(psrc, pdest, srcBottomRowStart + i, destBottomRowStart + (i * 3), destPitch);
            }

            int srcIndex;
            int destIndex;

            for (int y = 1; y < height - 1; y++)
            {
                srcIndex = y * width;
                destIndex = destPitch * y * 3;

                CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);
                srcIndex++;
                destIndex += 3;

                for (int x = 1; x < width - 1; x++)
                {
                    ExpandPixel(psrc, width, pdest, destPitch, srcIndex, destIndex);

                    srcIndex++;
                    destIndex += 3;
                }

                CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);
            }

        }
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void VectorScale(IntPtr source, IntPtr destination)
    {
        unsafe
        {
            uint* psrc = (uint*)source.ToPointer();
            uint* pdest = (uint*)destination;
            int width = SourceWidth;
            int height = SourceHeight;

            int destPitch = width * 3;

            int srcBottomRowStart = width * (height - 1);
            int destBottomRowStart = (destPitch * (height - 1)) * 3;

            for (int i = 0; i < width; i++)
            {
                CopyToDest(psrc, pdest, i, i * 3, destPitch);
                CopyToDest(psrc, pdest, srcBottomRowStart + i, destBottomRowStart + (i * 3), destPitch);
            }

            int srcIndex;
            int destIndex;

            int rowRemainder = (width - 2) % Vector<uint>.Count;

            for (int y = 1; y < height - 1; y++)
            {
                srcIndex = y * width;
                destIndex = y * 3 * width * 3;

                CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);

                srcIndex++;
                destIndex += 3;

                var aSpan = new ReadOnlySpan<uint>(psrc + srcIndex - width - 1, width - 2);
                var bSpan = new ReadOnlySpan<uint>(psrc + srcIndex - width, width - 2);
                var cSpan = new ReadOnlySpan<uint>(psrc + srcIndex - width + 1, width - 2);
                var dSpan = new ReadOnlySpan<uint>(psrc + srcIndex - 1, width - 2);
                var eSpan = new ReadOnlySpan<uint>(psrc + srcIndex, width - 2);
                var fSpan = new ReadOnlySpan<uint>(psrc + srcIndex + 1, width - 2);
                var gSpan = new ReadOnlySpan<uint>(psrc + srcIndex + width - 1, width - 2);
                var hSpan = new ReadOnlySpan<uint>(psrc + srcIndex + width, width - 2);
                var iSpan = new ReadOnlySpan<uint>(psrc + srcIndex + width + 1, width - 2);

                var aVec = MemoryMarshal.Cast<uint, Vector<uint>>(aSpan);
                var bVec = MemoryMarshal.Cast<uint, Vector<uint>>(bSpan);
                var cVec = MemoryMarshal.Cast<uint, Vector<uint>>(cSpan);
                var dVec = MemoryMarshal.Cast<uint, Vector<uint>>(dSpan);
                var eVec = MemoryMarshal.Cast<uint, Vector<uint>>(eSpan);
                var fVec = MemoryMarshal.Cast<uint, Vector<uint>>(fSpan);
                var gVec = MemoryMarshal.Cast<uint, Vector<uint>>(gSpan);
                var hVec = MemoryMarshal.Cast<uint, Vector<uint>>(hSpan);
                var iVec = MemoryMarshal.Cast<uint, Vector<uint>>(iSpan);

                for (int i = 0; i < aVec.Length; i++)
                {
                    var o1 = Vector.ConditionalSelect(
                        Vector.AndNot(Vector.AndNot(Vector.Equals(dVec[i], bVec[i]), Vector.Equals(dVec[i], hVec[i])), Vector.Equals(bVec[i], fVec[i])),
                        dVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3] = o1[j];

                    var o2 = Vector.ConditionalSelect(
                        Vector.BitwiseOr(
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(dVec[i], bVec[i]), Vector.Equals(dVec[i], hVec[i])), Vector.Equals(bVec[i], fVec[i])), Vector.Equals(eVec[i], cVec[i])),
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(bVec[i], fVec[i]), Vector.Equals(bVec[i], dVec[i])), Vector.Equals(fVec[i], hVec[i])), Vector.Equals(eVec[i], aVec[i]))
                        ),
                        bVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + 1] = o2[j];

                    var o3 = Vector.ConditionalSelect(
                        Vector.AndNot(Vector.AndNot(Vector.Equals(bVec[i], fVec[i]), Vector.Equals(bVec[i], dVec[i])), Vector.Equals(fVec[i], hVec[i])),
                        fVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + 2] = o3[j];

                    var o4 = Vector.ConditionalSelect(
                        Vector.BitwiseOr(
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(hVec[i], dVec[i]), Vector.Equals(hVec[i], fVec[i])), Vector.Equals(dVec[i], bVec[i])), Vector.Equals(eVec[i], aVec[i])),
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(dVec[i], bVec[i]), Vector.Equals(dVec[i], hVec[i])), Vector.Equals(bVec[i], fVec[i])), Vector.Equals(eVec[i], gVec[i]))
                        ),
                        dVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + destPitch] = o4[j];

                    var o5 = eVec[i];
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + destPitch + 1] = o5[j];

                    var o6 = Vector.ConditionalSelect(
                        Vector.BitwiseOr(
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(bVec[i], fVec[i]), Vector.Equals(bVec[i], dVec[i])), Vector.Equals(fVec[i], hVec[i])), Vector.Equals(eVec[i], iVec[i])),
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(fVec[i], hVec[i]), Vector.Equals(fVec[i], bVec[i])), Vector.Equals(hVec[i], dVec[i])), Vector.Equals(eVec[i], cVec[i]))
                        ),
                        fVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + destPitch + 2] = o6[j];

                    var o7 = Vector.ConditionalSelect(
                        Vector.AndNot(Vector.AndNot(Vector.Equals(hVec[i], dVec[i]), Vector.Equals(hVec[i], fVec[i])), Vector.Equals(dVec[i], bVec[i])),
                        dVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + destPitch + destPitch] = o7[j];

                    var o8 = Vector.ConditionalSelect(
                        Vector.BitwiseOr(
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(fVec[i], hVec[i]), Vector.Equals(fVec[i], bVec[i])), Vector.Equals(hVec[i], dVec[i])), Vector.Equals(eVec[i], gVec[i])),
                            Vector.AndNot(Vector.AndNot(Vector.AndNot(Vector.Equals(hVec[i], dVec[i]), Vector.Equals(hVec[i], fVec[i])), Vector.Equals(dVec[i], bVec[i])), Vector.Equals(eVec[i], iVec[i]))
                        ),
                        hVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + destPitch + destPitch + 1] = o8[j];

                    var o9 = Vector.ConditionalSelect(
                        Vector.AndNot(Vector.AndNot(Vector.Equals(fVec[i], hVec[i]), Vector.Equals(fVec[i], bVec[i])), Vector.Equals(hVec[i], dVec[i])),
                        fVec[i],
                        eVec[i]
                    );
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        pdest[destIndex + j * 3 + destPitch + destPitch + 2] = o9[j];

                    destIndex += Vector<uint>.Count * 3;
                    srcIndex += Vector<uint>.Count;
                }

                for (int i = 0; i < rowRemainder; i++)
                {
                    ExpandPixel(psrc, width, pdest, destPitch, srcIndex, destIndex);
                    srcIndex++;
                    destIndex += 3;
                }

                CopyToDest(psrc, pdest, srcIndex, destIndex, destPitch);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ExpandPixel(uint* psrc, int width, uint* pdest, int destPitch, int srcIndex, int destIndex)
    {
        uint a = psrc[srcIndex - width - 1];
        uint b = psrc[srcIndex - width];
        uint c = psrc[srcIndex - width + 1];
        uint d = psrc[srcIndex - 1];
        uint e = psrc[srcIndex];
        uint f = psrc[srcIndex + 1];
        uint g = psrc[srcIndex + width - 1];
        uint h = psrc[srcIndex + width];
        uint i = psrc[srcIndex + width + 1];

        uint o1 = e;
        uint o2 = o1;
        uint o3 = o1;
        uint o4 = o1;
        uint o5 = o1;
        uint o6 = o1;
        uint o7 = o1;
        uint o8 = o1;
        uint o9 = o1;

        if (d == b && d != h && b != f) {
            o1 = d;
        }

        if ((d == b && d != h && b != f && e != c) || (b == f && b != d && f != h && e != a)) {
            o2 = b;
        }

        if (b == f && b != d && f != h) {
            o3 = f;
        }

        if ((h == d && h != f && d != b && e != a) || (d == b && d != h && b != f && e != g)) {
            o4 = d;
        }

        o5 = e;

        if ((b == f && b != d && f != h && e != i) || (f == h && f != b && h != d && e != c)) {
            o6 = f;
        }

        if (h == d && h != f && d != b) {
            o7 = d;
        }

        if ((f == h && f != b && h != d && e != g) || (h == d && h != f && d != b && e != i)) {
            o8 = h;
        }

        if (f == h && f != b && h != d) {
            o9 = f;
        }

        pdest[destIndex] = o1;
        pdest[destIndex + 1] = o2;
        pdest[destIndex + 2] = o3;
        pdest[destIndex + destPitch] = o4;
        pdest[destIndex + destPitch + 1] = o5;
        pdest[destIndex + destPitch + 2] = o6;
        pdest[destIndex + destPitch + destPitch] = o7;
        pdest[destIndex + destPitch + destPitch + 1] = o8;
        pdest[destIndex + destPitch + destPitch + 2] = o9;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void CopyToDest(uint* psrc, uint* pdest, int srcIndex, int destIndex, int destPitch)
    {
        uint value = psrc[srcIndex];
        pdest[destIndex] = value;
        pdest[destIndex + 1] = value;
        pdest[destIndex + 2] = value;
        pdest[destIndex + destPitch] = value;
        pdest[destIndex + destPitch + 1] = value;
        pdest[destIndex + destPitch + 2] = value;
        pdest[destIndex + destPitch + destPitch] = value;
        pdest[destIndex + destPitch + destPitch + 1] = value;
        pdest[destIndex + destPitch + destPitch + 2] = value;
    }
}