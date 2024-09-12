namespace Spice86.Core.Emulator.Devices.Sound;

public static class MathUtils {
    public static float DecibelToGain(float decibel) =>
        (float)Math.Pow(10.0f, decibel / 20.0f);
}