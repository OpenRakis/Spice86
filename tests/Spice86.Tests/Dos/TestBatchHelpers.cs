namespace Spice86.Tests.Dos;

using Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

/// <summary>
/// Test environment implementation for testing environment variable expansion in batch processing.
/// </summary>
public sealed class TestBatchEnvironment : IBatchEnvironment {
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets an environment variable value.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    public void SetVariable(string name, string value) => _variables[name] = value;

    /// <inheritdoc/>
    public string? GetEnvironmentValue(string name) =>
        _variables.TryGetValue(name, out string? value) ? value : null;
}

/// <summary>
/// Test line reader implementation that reads from an array of strings.
/// </summary>
public sealed class TestStringLineReader : IBatchLineReader {
    private readonly string[] _lines;
    private int _index = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestStringLineReader"/> class.
    /// </summary>
    /// <param name="lines">The lines to read.</param>
    public TestStringLineReader(string[] lines) => _lines = lines;

    /// <inheritdoc/>
    public string? ReadLine() => _index < _lines.Length ? _lines[_index++] : null;

    /// <inheritdoc/>
    public bool Reset() {
        _index = 0;
        return true;
    }

    /// <inheritdoc/>
    public void Dispose() { }
}
