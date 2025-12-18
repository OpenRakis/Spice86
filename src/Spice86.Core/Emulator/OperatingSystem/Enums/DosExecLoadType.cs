namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Load type for INT 21h AH=4Bh EXEC.
/// </summary>
public enum DosExecLoadType : byte {
    LoadAndExecute = 0x00,
    LoadOnly = 0x01,
    LoadOverlay = 0x03
}
