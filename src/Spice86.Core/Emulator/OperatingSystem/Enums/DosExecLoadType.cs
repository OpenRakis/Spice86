namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// DOS EXEC (INT 21h, AH=4Bh) load types.
/// </summary>
/// <remarks>
/// Based on RBIL documentation for INT 21h/AH=4Bh.
/// The load type is specified in the AL register.
/// </remarks>
public enum DosExecLoadType : byte {
    /// <summary>
    /// Load and execute program (AL=00h).
    /// The child program is loaded and executed, and control returns
    /// to the parent when the child terminates.
    /// </summary>
    LoadAndExecute = 0x00,

    /// <summary>
    /// Load but do not execute (AL=01h).
    /// The program is loaded into memory but not executed.
    /// The entry point (CS:IP) and stack (SS:SP) are returned in the parameter block.
    /// Used by debuggers.
    /// </summary>
    LoadOnly = 0x01,

    /// <summary>
    /// Load overlay (AL=03h).
    /// Loads the program at a specified segment without creating a PSP.
    /// Used to load overlays into an existing program's memory space.
    /// </summary>
    LoadOverlay = 0x03,

    /// <summary>
    /// Load and execute in background (AL=04h).
    /// European MS-DOS 4.0 only. Not commonly supported.
    /// </summary>
    LoadAndExecuteBackground = 0x04
}
