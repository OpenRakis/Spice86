namespace Spice86.Core.Emulator.Devices.Sound;

public static class MathUtils {
    public static double decibel_to_gain(double decibel) =>
        Math.Pow(10.0f, decibel / 20.0f);
}