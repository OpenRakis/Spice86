using System.IO;

namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Provides <see cref="IDataSource"/> access backed by a file on disk.</summary>
public sealed class FileBackedDataSource : IDataSource, IDisposable {
    private readonly FileStream _stream;

    /// <summary>Opens <paramref name="filePath"/> for reading.</summary>
    /// <param name="filePath">Absolute or relative path to the file.</param>
    /// <exception cref="IOException">Thrown when the file cannot be opened.</exception>
    public FileBackedDataSource(string filePath) {
        try {
            _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        } catch (IOException) {
            throw;
        }
    }

    /// <inheritdoc/>
    public long LengthBytes => _stream.Length;

    /// <inheritdoc/>
    public int Read(long byteOffset, Span<byte> destination) {
        _stream.Seek(byteOffset, SeekOrigin.Begin);
        return _stream.Read(destination);
    }

    /// <summary>Closes the underlying file stream.</summary>
    public void Dispose() {
        _stream.Dispose();
    }
}
