namespace Spice86.Shared.Utils;

/// <summary>
/// Utility methods for working with bit masks.
/// </summary>
public class BitMaskUtils {
    /// <summary>
    /// Creates a bit mask from a list of bit positions.
    /// </summary>
    /// <param name="bitPositions">The collection of bit positions to set in the mask.</param>
    /// <returns>A 32-bit unsigned integer with bits set at the specified positions.</returns>
    public static uint BitMaskFromBitList(IEnumerable<int> bitPositions) {
        uint mask = 0;
        foreach (int bitPosition in bitPositions) {
            mask |= 1u << bitPosition;
        }
        return mask;
    }
}