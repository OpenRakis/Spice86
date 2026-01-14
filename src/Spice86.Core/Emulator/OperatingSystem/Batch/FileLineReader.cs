namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.Memory;
using System;
using System.Text;
using System.IO;

/// <summary>
/// Reads lines from a DOS file.
/// </summary>
public class FileLineReader : ILineReader, IDisposable {
    private readonly VirtualFileBase _file;
    private readonly long _startPosition;
    private bool _isEof;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLineReader"/> class.
    /// </summary>
    /// <param name="file">The DOS file to read from.</param>
    public FileLineReader(VirtualFileBase file) {
        _file = file;
        _startPosition = file.Position;
        _isEof = false;
        _disposed = false;
    }

    /// <inheritdoc/>
    public string? ReadLine() {
        if (_isEof) {
            return null;
        }

        StringBuilder line = new StringBuilder(256);
        bool foundNewline = false;

        while (!foundNewline && !_isEof) {
            byte[] buffer = new byte[1];
            int bytesRead = _file.Read(buffer, 0, 1);

            if (bytesRead == 0) {
                _isEof = true;
                if (line.Length == 0) {
                    return null;
                }
                break;
            }

            byte b = buffer[0];
            if (b == 0x0A) {
                foundNewline = true;
            } else if (b != 0x0D) {
                line.Append((char)b);
            }
        }

        return line.ToString();
    }

    /// <inheritdoc/>
    public void Reset() {
        _file.Seek(_startPosition, SeekOrigin.Begin);
        _isEof = false;
    }

    /// <summary>
    /// Disposes the file line reader and its underlying file.
    /// </summary>
    public void Dispose() {
        if (!_disposed) {
            _file?.Dispose();
            _disposed = true;
        }
    }
}
