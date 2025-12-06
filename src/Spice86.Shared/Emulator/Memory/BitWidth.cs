namespace Spice86.Shared.Emulator.Memory;

/// <summary>
/// Specifies the width of data in bits for memory operations.
/// </summary>
public enum BitWidth {
    /// <summary>
    /// 8-bit byte width.
    /// </summary>
    BYTE_8,
    
    /// <summary>
    /// 16-bit word width.
    /// </summary>
    WORD_16,
    
    /// <summary>
    /// 32-bit double word width.
    /// </summary>
    DWORD_32
}