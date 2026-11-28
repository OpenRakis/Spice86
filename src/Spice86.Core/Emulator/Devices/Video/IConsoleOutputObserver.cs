namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// Observes every character written to the console, whatever service produced it: DOS character
/// output and BIOS teletype output both reach the video functionality, which reports each
/// character it writes, control characters included.
/// </summary>
public interface IConsoleOutputObserver {
    /// <summary>Called for every character written to the console, in write order.</summary>
    /// <param name="character">Character written.</param>
    void OnCharacter(char character);
}
