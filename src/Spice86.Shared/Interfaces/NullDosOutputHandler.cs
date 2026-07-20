namespace Spice86.Shared.Interfaces;

/// <summary>
/// No-op implementation of <see cref="IDosOutputHandler"/> used when DOS output logging is disabled.
/// </summary>
public sealed class NullDosOutputHandler : IDosOutputHandler {
    /// <inheritdoc />
    public void OnCharacterOutput(char character) {
        // Intentionally empty — output capture is disabled.
    }
}
