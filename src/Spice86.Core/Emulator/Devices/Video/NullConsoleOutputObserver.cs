namespace Spice86.Core.Emulator.Devices.Video;

/// <summary><see cref="IConsoleOutputObserver"/> that records nothing, the default.</summary>
public sealed class NullConsoleOutputObserver : IConsoleOutputObserver {
    /// <inheritdoc />
    public void OnCharacter(char character) {
        //NOP
    }
}
