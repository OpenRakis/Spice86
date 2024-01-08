namespace Spice86.Core.Emulator.Devices.Video;

internal static class BitManipulationExtensions {
    /// <summary>
    ///     Rotate right by the specified amount.
    /// </summary>
    public static void Ror(ref this byte value, int amount) {
        value = (byte)(value << 8 - amount | value >> amount);
    }

    /// <summary>
    ///     Split a byte into an array of eight booleans.
    /// </summary>
    public static bool[] ToBits(this byte value) {
        bool[] b = new bool[8];
        for (int i = 0; i < 8; i++) {
            b[i] = (value & 1 << i) != 0;
        }
        return b;
    }
}