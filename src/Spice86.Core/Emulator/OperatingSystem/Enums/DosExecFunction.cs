namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// EXEC function selector for INT 21h AH=4Bh.
/// </summary>
public enum DosExecFunction : byte
{
    /// <summary>Load and execute.</summary>
    LoadAndExecute = 0x00,

    /// <summary>Load, but do not begin execution (returns entry and stack in the block).</summary>
    Load = 0x01,

    /// <summary>Load overlay at specified segment with relocation factor.</summary>
    LoadOverlay = 0x03
}