using System.Collections.Frozen;

namespace Spice86.Core.Emulator.Devices.Video;

internal static class BitManipulationExtensions {
    private static readonly FrozenDictionary<byte, bool[]> BitLookupTable = InitializeBitLookupTable();

    private static FrozenDictionary<byte, bool[]> InitializeBitLookupTable() {
        var table = new Dictionary<byte, bool[]>(256);
        for (int value = 0; value < 256; value++) {
            byte key = (byte)value;
            bool[] bits = new bool[8];
            for (int i = 0; i < 8; i++) {
                bits[i] = (value & (1 << i)) != 0;
            }
            table[key] = bits;
        }
        return table.ToFrozenDictionary();
    }

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
        bool[] cached = BitLookupTable[value];
        bool[] result = new bool[8];
        Array.Copy(cached, result, 8);
        return result;
    }
}