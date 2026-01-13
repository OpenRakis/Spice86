namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

/// <summary>
/// A batch line reader that reads from an in-memory string array.
/// </summary>
public sealed class StringArrayLineReader : IBatchLineReader {
    private readonly string[] _lines;
    private int _currentIndex;

    /// <summary>
    /// Initializes a new instance with the given lines.
    /// </summary>
    /// <param name="lines">The lines to read.</param>
    public StringArrayLineReader(string[] lines) {
        _lines = lines;
        _currentIndex = 0;
    }

    /// <inheritdoc/>
    public string? ReadLine() {
        if (_currentIndex >= _lines.Length) {
            return null;
        }
        return _lines[_currentIndex++];
    }

    /// <inheritdoc/>
    public bool Reset() {
        _currentIndex = 0;
        return true;
    }

    /// <inheritdoc/>
    public void Dispose() {
        // Nothing to dispose for in-memory strings
    }
}
