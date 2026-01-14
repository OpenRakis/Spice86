namespace Spice86.Core.Emulator.OperatingSystem.Batch;

/// <summary>
/// Interface for reading lines from various sources (files, strings, etc.).
/// </summary>
public interface ILineReader {
    /// <summary>
    /// Reads the next line from the source.
    /// </summary>
    /// <returns>The next line, or null if end of source is reached.</returns>
    string? ReadLine();

    /// <summary>
    /// Resets the reader to the beginning of the source.
    /// </summary>
    void Reset();
}
