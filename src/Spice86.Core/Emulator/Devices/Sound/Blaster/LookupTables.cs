namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

internal static class LookupTables {
    public static readonly float[] U8To16 = new float[256];
    public static readonly float[] S8To16 = new float[256];

    static LookupTables() {
        for (int i = 0; i <= 255; i++) {
            U8To16[i] = U8To16Func(i);
            S8To16[i] = S8To16Func((sbyte)(i - 128));
        }
    }

    private static float U8To16Func(int uVal) {
        const int Max16BitSampleValue = 32767;
        int sVal = uVal - 128;
        if (sVal > 0) {
            return (float)Math.Round(sVal * (Max16BitSampleValue / 127.0));
        }
        return sVal * 256;
    }

    private static float S8To16Func(sbyte sVal) {
        const int Max16BitSampleValue = 32767;
        if (sVal > 0) {
            return (float)Math.Round(sVal * (Max16BitSampleValue / 127.0));
        }
        return sVal * 256;
    }
}
