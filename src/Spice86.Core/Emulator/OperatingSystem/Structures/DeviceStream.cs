namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Linq;

using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <summary>
/// Provides a generic view of a sequence of bytes, for a DOS Device.
/// </summary>
public class DeviceStream : Stream {
    private readonly string _deviceName;
    private readonly ILoggerService _logger;
    private long _length = int.MaxValue;

    /// <inheritdoc />
    public override bool CanRead { get; }

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite { get; }

    /// <inheritdoc />
    public override long Length => _length;

    /// <inheritdoc />
    public override long Position { get; set; }


    /// <inheritdoc />
    public DeviceStream(string deviceName, string openMode, ILoggerService logger) {
        _deviceName = deviceName;
        _logger = logger;
        CanRead = openMode.Contains('r');
        CanWrite = openMode.Contains('w');
    }

    /// <inheritdoc />
    public override void Flush() {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) {
        if (!CanRead) {
            throw new NotSupportedException();
        }

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Reading {Count} bytes from device {DeviceName} at offset {Offset}",
                count, _deviceName, offset);
        }

        Position += count;
        return count;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) {
        if (!CanSeek) {
            throw new NotSupportedException();
        }

        if (origin == SeekOrigin.Begin) {
            Position = offset;
        } else if (origin == SeekOrigin.Current) {
            Position += offset;
        } else if (origin == SeekOrigin.End) {
            Position = Length - offset;
        }

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Changing position in device {DeviceName} to {Position}",
                _deviceName, Position);
        }

        return Position;
    }

    /// <inheritdoc />
    public override void SetLength(long value) {
        _length = value;
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) {
        if (!CanWrite) {
            throw new NotSupportedException();
        }

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Writing {Count} bytes to device {DeviceName} at position {Position}",
                count, _deviceName, offset);
            byte[] bytes = buffer.Skip(offset).Take(count).ToArray();
            _logger.Debug("{Bytes}", BitConverter.ToString(bytes));
        }

        Position += count;
    }
}