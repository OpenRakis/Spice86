namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Drives the INT21H Load and/or exec function behavior
/// </summary>
public enum DosExecuteMode : byte {
    /// <summary>
    /// Load and execute the program
    /// </summary>
    LoadAndExecute = 0,
    /// <summary>
    /// Load, create the program header, but do not run execution
    /// </summary>
    LoadButDoNotRun = 1,
    /// <summary>
    /// Load overlay. No header created.
    /// </summary>
    LoadOverlay = 3
}