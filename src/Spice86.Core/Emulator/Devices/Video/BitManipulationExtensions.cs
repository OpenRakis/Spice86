using System.Collections.Frozen;

namespace Spice86.Core.Emulator.Devices.Video;

internal static class BitManipulationExtensions {
    /// <summary>
    ///     Rotate right by the specified amount.
    /// </summary>
    public static void Ror(ref this byte value, int amount) {
        value = (byte)(value << (8 - amount) | value >> amount);
    }
}