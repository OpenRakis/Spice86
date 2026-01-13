namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

using System;

/// <summary>
/// Provides access to DOS environment variables through the DOS kernel.
/// </summary>
/// <remarks>
/// TODO: This implementation should integrates with the actual DOS environment block
/// for full emulation compatibility, following DOSBox staging's pattern.
/// </remarks>
public sealed class DosEnvironmentAdapter : IBatchEnvironment {
    private readonly Func<string, string?> _getEnvironmentValue;

    /// <summary>
    /// Initializes a new instance with a delegate to retrieve environment values.
    /// </summary>
    /// <param name="getEnvironmentValue">
    /// A function that retrieves the value of an environment variable by name.
    /// Returns null if the variable is not found.
    /// </param>
    public DosEnvironmentAdapter(Func<string, string?> getEnvironmentValue) {
        _getEnvironmentValue = getEnvironmentValue;
    }

    /// <inheritdoc/>
    public string? GetEnvironmentValue(string name) {
        return _getEnvironmentValue(name);
    }
}
