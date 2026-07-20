namespace Spice86.Shared.Interfaces;

/// <summary>
/// Handles DOS program text output (characters written to stdout/stderr via INT 21h).
/// Implementations can log, buffer, or forward the output to an external sink.
/// </summary>
public interface IDosOutputHandler {
    /// <summary>
    /// Called when the DOS program writes a character to the console.
    /// </summary>
    /// <param name="character">The character written by the DOS program.</param>
    void OnCharacterOutput(char character);
}
