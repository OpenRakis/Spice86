namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

using System.IO;
using System.Text;

/// <summary>
/// Reads batch file lines from the host file system.
/// </summary>
/// <remarks>
/// TODO: This implementation reads directly from the host file system !
/// </remarks>
public sealed class HostFileLineReader : IBatchLineReader {
    private readonly string _filePath;
    private StreamReader? _reader;

    /// <summary>
    /// Initializes a new reader for the specified file.
    /// </summary>
    /// <param name="filePath">Full path to the batch file on the host file system.</param>
    public HostFileLineReader(string filePath) {
        _filePath = filePath;
        // Note: For full DOS compatibility, CP437 would be preferred but requires
        // registering System.Text.Encoding.CodePages provider at startup
        _reader = new StreamReader(filePath, Encoding.ASCII);
    }

    /// <inheritdoc/>
    public string? ReadLine() => _reader?.ReadLine();

    /// <inheritdoc/>
    public bool Reset() {
        if (_reader is null) {
            return false;
        }
        _reader.BaseStream.Seek(0, SeekOrigin.Begin);
        _reader.DiscardBufferedData();
        return true;
    }

    /// <inheritdoc/>
    public void Dispose() {
        _reader?.Dispose();
        _reader = null;
    }
}
