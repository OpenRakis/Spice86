namespace Spice86.Core.Emulator.OperatingSystem.Batch;

/// <summary>
/// Executes batch display-related built-in commands without coupling batch execution to a specific video subsystem.
/// </summary>
public interface IBatchDisplayCommandHandler {
    /// <summary>
    /// Clears the active text screen and resets the cursor position.
    /// </summary>
    void ClearScreen();
}