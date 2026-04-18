namespace Spice86.Shared.Emulator.Memory;

public enum BitWidth {
    // Explicit values correspond to the size in bits.
    BOOL_1 = 1,
    NIBBLE_4 = 4,
    QUIBBLE_5 = 5,
    BYTE_8 = 8,
    WORD_16 = 16,
    DWORD_32 = 32,
    QWORD_64 = 64
}