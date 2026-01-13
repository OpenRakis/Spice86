namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

using System;

/// <summary>
/// Represents the context of a batch file being processed.
/// </summary>
internal sealed class BatchContext : IDisposable {
    private readonly IBatchLineReader _reader;
    private readonly IBatchEnvironment _environment;
    private readonly string[] _parameters;
    private int _shiftOffset;

    /// <summary>
    /// Gets the full path to the batch file.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets or sets the parent batch context (for CALL).
    /// </summary>
    public BatchContext? Parent { get; set; }

    /// <summary>
    /// Gets the saved echo state from when this batch started.
    /// </summary>
    public bool SavedEchoState { get; }

    /// <summary>
    /// Initializes a new batch context.
    /// </summary>
    /// <param name="filePath">Path to the batch file.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="currentEchoState">Current echo state to save.</param>
    /// <param name="reader">The line reader for batch file content.</param>
    /// <param name="environment">The environment for variable expansion.</param>
    public BatchContext(string filePath, string[] arguments, bool currentEchoState,
                        IBatchLineReader reader, IBatchEnvironment environment) {
        FilePath = filePath;
        SavedEchoState = currentEchoState;
        _reader = reader;
        _environment = environment;

        // Build parameters array: %0 is the batch file name, %1-%9 are arguments
        _parameters = new string[10];
        _parameters[0] = filePath;
        for (int i = 0; i < Math.Min(9, arguments.Length); i++) {
            _parameters[i + 1] = arguments[i];
        }
        // Fill remaining with empty strings
        for (int i = arguments.Length + 1; i < 10; i++) {
            _parameters[i] = string.Empty;
        }
    }

    /// <summary>
    /// Initializes a new batch context using the host file system.
    /// </summary>
    /// <param name="filePath">Path to the batch file.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="currentEchoState">Current echo state to save.</param>
    /// <remarks>
    /// Convenience constructor for creating a batch context using the host file system.
    /// For full DOS integration, use the constructor with IBatchLineReader and IBatchEnvironment.
    /// </remarks>
    public BatchContext(string filePath, string[] arguments, bool currentEchoState)
        : this(filePath, arguments, currentEchoState,
               new HostFileLineReader(filePath), EmptyBatchEnvironment.Instance) {
    }

    /// <summary>
    /// Gets a parameter by index (0-9).
    /// </summary>
    /// <param name="index">Parameter index (0 = batch file name, 1-9 = arguments).</param>
    /// <returns>The parameter value, or empty string if not defined.</returns>
    public string GetParameter(int index) {
        // Apply shift offset for parameters 1-9 (not %0 which is always the batch file)
        int actualIndex = index == 0 ? 0 : index + _shiftOffset;
        if (actualIndex < 0 || actualIndex >= _parameters.Length) {
            return string.Empty;
        }
        return _parameters[actualIndex];
    }

    /// <summary>
    /// Shifts the parameters by one position.
    /// After SHIFT, %1 becomes what was %2, %2 becomes what was %3, etc.
    /// </summary>
    public void Shift() {
        _shiftOffset++;
    }

    /// <summary>
    /// Reads the next line from the batch file.
    /// </summary>
    /// <returns>The next line, or null if end of file.</returns>
    public string? ReadLine() {
        return _reader.ReadLine();
    }

    /// <summary>
    /// Seeks to a label in the batch file.
    /// </summary>
    /// <param name="label">The label to find (without leading colon).</param>
    /// <returns>True if found, false otherwise.</returns>
    public bool SeekToLabel(string label) {
        // Reset to beginning of file
        if (!_reader.Reset()) {
            return false;
        }

        string labelToFind = label.ToUpperInvariant();

        while (true) {
            string? line = _reader.ReadLine();
            if (line is null) {
                // End of file - label not found
                return false;
            }

            line = line.Trim();
            if (line.Length == 0 || line[0] != ':') {
                continue;
            }

            // Extract the label from the line
            string lineLabel = line[1..].Trim();
            
            // Label ends at first whitespace (find the first whitespace character)
            int spaceIndex = -1;
            for (int i = 0; i < lineLabel.Length; i++) {
                if (char.IsWhiteSpace(lineLabel[i])) {
                    spaceIndex = i;
                    break;
                }
            }
            if (spaceIndex >= 0) {
                lineLabel = lineLabel[..spaceIndex];
            }

            if (lineLabel.ToUpperInvariant() == labelToFind) {
                return true;
            }
        }
    }

    /// <summary>
    /// Gets the environment value for a variable name.
    /// </summary>
    /// <param name="name">Variable name (case-insensitive).</param>
    /// <returns>The value, or null if not found.</returns>
    public string? GetEnvironmentValue(string name) {
        return _environment.GetEnvironmentValue(name);
    }

    /// <summary>
    /// Disposes the batch context.
    /// </summary>
    public void Dispose() {
        _reader.Dispose();
    }
}
