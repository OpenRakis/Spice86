namespace Spice86.Shared.Utils;

public class BitMaskUtils {
    public static uint BitMaskFromBitList(IEnumerable<int> bitPositions) {
        uint mask = 0;
        foreach (int bitPosition in bitPositions) {
            mask |= 1u << bitPosition;
        }
        return mask;
    }
}