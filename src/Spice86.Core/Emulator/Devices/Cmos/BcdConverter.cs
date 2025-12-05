namespace Spice86.Core.Emulator.Devices.Cmos;

/// <summary>
/// Utility class for converting between Binary Coded Decimal (BCD) and binary formats.
/// BCD encoding stores each decimal digit in a nibble (4 bits), allowing values 0-99 to be
/// represented in a single byte (e.g., decimal 47 = 0x47 in BCD = 0100 0111).
/// </summary>
public static class BcdConverter {
    /// <summary>
    /// Converts a binary value (0-99) to BCD format.
    /// </summary>
    /// <param name="binary">The binary value to convert (must be 0-99).</param>
    /// <returns>The BCD-encoded value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is greater than 99.</exception>
    public static byte ToBcd(byte binary) {
        if (binary > 99) {
            throw new ArgumentOutOfRangeException(nameof(binary), binary,
                $"Value must be 0-99 for BCD encoding. Value was {binary}.");
        }
        int tens = binary / 10;
        int ones = binary % 10;
        return (byte)((tens << 4) | ones);
    }

    /// <summary>
    /// Converts a BCD-encoded value to binary format (0-99).
    /// </summary>
    /// <param name="bcd">The BCD value to convert.</param>
    /// <returns>The binary value (0-99).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the BCD value is invalid (nibbles > 9).</exception>
    public static byte FromBcd(byte bcd) {
        int highNibble = (bcd >> 4) & 0x0F;
        int lowNibble = bcd & 0x0F;

        // Validate BCD: each nibble must be 0-9
        if (highNibble > 9 || lowNibble > 9) {
            throw new ArgumentOutOfRangeException(nameof(bcd), bcd,
                $"Invalid BCD value: 0x{bcd:X2}. Each nibble must be 0-9 (high={highNibble}, low={lowNibble}).");
        }

        return (byte)((highNibble * 10) + lowNibble);
    }
}