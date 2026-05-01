namespace Spice86.Shared.Emulator.Memory;

/// <summary>
/// Extension methods for <see cref="BitWidth"/>.
/// </summary>
public static class BitWidthExtensions {
    /// <summary>
    /// Returns the next wider BitWidth (e.g. BYTE_8 -> WORD_16).
    /// </summary>
    public static BitWidth Double(this BitWidth bitWidth) {
        return bitWidth switch {
            BitWidth.BYTE_8 => BitWidth.WORD_16,
            BitWidth.WORD_16 => BitWidth.DWORD_32,
            BitWidth.DWORD_32 => BitWidth.QWORD_64,
            _ => throw new ArgumentOutOfRangeException(nameof(bitWidth), bitWidth, "Cannot double this BitWidth")
        };
    }

    /// <summary>
    /// Returns the size in bytes (e.g. WORD_16 -> 2).
    /// </summary>
    public static int ToBytes(this BitWidth bitWidth) {
        return (int)bitWidth / 8;
    }
}
