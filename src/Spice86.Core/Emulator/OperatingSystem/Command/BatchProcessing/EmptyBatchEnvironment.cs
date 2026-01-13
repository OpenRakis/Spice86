namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

/// <summary>
/// A batch environment that always returns null (no environment variables).
/// </summary>
/// <remarks>
/// This is useful for testing when no environment is needed.
/// </remarks>
public sealed class EmptyBatchEnvironment : IBatchEnvironment {
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static EmptyBatchEnvironment Instance { get; } = new();

    private EmptyBatchEnvironment() { }

    /// <inheritdoc/>
    public string? GetEnvironmentValue(string name) => null;
}
