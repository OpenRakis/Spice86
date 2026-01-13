namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

using System;

/// <summary>
/// Provides an interface for reading lines from a batch file.
/// </summary>
public interface IBatchLineReader : IDisposable {
    /// <summary>
    /// Reads the next line from the batch file.
    /// </summary>
    /// <returns>The next line, or null if end of file or error.</returns>
    string? ReadLine();

    /// <summary>
    /// Resets the reader to the beginning of the file.
    /// </summary>
    /// <returns>True if reset was successful, false otherwise.</returns>
    bool Reset();
}
