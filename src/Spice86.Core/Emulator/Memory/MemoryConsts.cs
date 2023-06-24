namespace Spice86.Core.Emulator.Memory; 

/// <summary>
/// Shared Memory constants
/// </summary>
public class MemoryConsts {
    /// <summary>
    /// This is the start of the HMA. <br/>
    /// This value is equal to 1 MB.
    /// </summary>
    public const uint StartOfHighMemoryArea = 0x100000;

    /// <summary>
    /// This is the end of the HMA. <br/>
    /// Real Mode cannot access memory beyond this. <br/>
    /// This value equals to 1 MB + 65 519 bytes.
    /// </summary>
    public const uint EndOfHighMemoryArea = 0x10FFEF;
}