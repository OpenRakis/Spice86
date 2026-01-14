namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.IO;

/// <summary>
/// Wrapper around a FileStream to implement VirtualFileBase.
/// Used for reading batch files directly from the host file system.
/// The FileStream is kept open for the lifetime of this object and should be closed when done.
/// </summary>
public sealed class SimpleVirtualFile : VirtualFileBase {
    private FileStream? _stream;
    private bool _disposed;

    public SimpleVirtualFile(FileStream stream, string fileName) {
        _stream = stream;
        Name = fileName;
        _disposed = false;
    }

    public override bool CanRead => !_disposed && (_stream?.CanRead ?? false);

    public override bool CanSeek => !_disposed && (_stream?.CanSeek ?? false);

    public override bool CanWrite => false;

    public override long Length => _disposed || _stream == null ? 0 : _stream.Length;

    public override long Position {
        get => _disposed || _stream == null ? 0 : _stream.Position;
        set {
            if (!_disposed && _stream != null) {
                _stream.Position = value;
            }
        }
    }

    public override void Flush() {
        if (!_disposed && _stream != null) {
            _stream.Flush();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (_disposed || _stream == null) {
            return 0;
        }
        return _stream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin) {
        if (_disposed || _stream == null) {
            return 0;
        }
        return _stream.Seek(offset, origin);
    }

    public override void SetLength(long value) {
        if (!_disposed && _stream != null) {
            _stream.SetLength(value);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException("SimpleVirtualFile is read-only");
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _stream?.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
