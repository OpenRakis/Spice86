namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Lookup tables for converting 8-bit and 16-bit audio samples to floating-point format.
/// </summary>
internal static class LookupTables {
    private const int Max16BitSampleValue = 32767;
    private const double PositiveScalar = Max16BitSampleValue / 127.0; // 257.834...

    public static readonly float[] U8To16 = new float[256];

    static LookupTables() {
        for (int i = 0; i < U8To16.Length; i++) {
            int signedValue = i - 128; // Center at zero: -128 to 127

            if (signedValue > 0) {
                U8To16[i] = (float)Math.Round(signedValue * PositiveScalar, MidpointRounding.AwayFromZero);
            } else {
                U8To16[i] = signedValue * 256;
            }
        }
    }

    public static float ToUnsigned8(byte sample) {
        return U8To16[sample];
    }

    public static float ToSigned8(sbyte sample) {
        return U8To16[sample + 128];
    }

    public static float ToUnsigned16(ushort sample) {
        short signedSample = (short)(sample - 0x8000);
        return signedSample;
    }

    public static float ToSigned16(short sample) {
        return sample;
    }
}