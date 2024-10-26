namespace Spice86.Core.Emulator.OperatingSystem.Enums;
public enum DosExecOperation : byte {
    /// <summary>
    /// Load and execute a new program
    /// </summary>
    LoadAndExecute = 0,
    /// <summary>
    /// Load the program and stay resident
    /// </summary>
    LoadOnly = 1,
    /// <summary>
    /// Load program overlay
    /// </summary>
    LoadOverlay = 2
}
