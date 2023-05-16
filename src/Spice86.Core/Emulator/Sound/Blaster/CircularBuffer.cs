namespace Spice86.Core.Emulator.Sound.Blaster;

using System;
using System.Threading;

/// <summary>
/// Stores bytes of data in a circular buffer.
/// </summary>
public sealed class CircularBuffer {
    private readonly byte[] _data;
    private readonly int _sizeMask;
    private volatile int _readPosition;
    private volatile int _writePosition;
    private volatile int _bytesInBuffer;

    /// <summary>
    /// Initializes a new instance of the CircularBuffer class.
    /// </summary>
    /// <param name="capacity">Size of the buffer in bytes; the value must be a power of two.</param>
    public CircularBuffer(int capacity) {
        _sizeMask = capacity - 1;
        _data = new byte[capacity];
    }

    /// <summary>
    /// Gets the size of the buffer in bytes.
    /// </summary>
    public int Capacity => _sizeMask + 1;

    /// <summary>
    /// Reads bytes from the buffer to an array and advances the read pointer.
    /// </summary>
    /// <param name="buffer">Buffer into which bytes are written.</param>
    /// <returns>Number of bytes actually read.</returns>
    public int Read(Span<byte> buffer) {
        int bufferBytes = _bytesInBuffer;
        int count = Math.Min(buffer.Length, bufferBytes);

        if (count > 0) {
            int readPos = _readPosition;
            if (count <= _data.Length - readPos) {
                Span<byte> source = _data.AsSpan(readPos, count);
                source.CopyTo(buffer);
            } else {
                Span<byte> src1 = _data.AsSpan(readPos, _data.Length - readPos);
                Span<byte> src2 = _data.AsSpan(0, count - src1.Length);
                src1.CopyTo(buffer);
                src2.CopyTo(buffer[src1.Length..]);
            }

            Interlocked.Add(ref _bytesInBuffer, -count);
            _readPosition = readPos + count & _sizeMask;
        }

        return count;
    }
    /// <summary>
    /// Writes bytes from a location in memory to the buffer and advances the write pointer.
    /// </summary>
    /// <param name="source">Data to read.</param>
    /// <returns>Number of bytes actually written.</returns>
    public int Write(ReadOnlySpan<byte> source) {
        int bytesAvailable = _bytesInBuffer;
        int bytesFree = Capacity - bytesAvailable;

        ReadOnlySpan<byte> sourceSpan = source.Length <= bytesFree ? source : source[..bytesFree];

        if (sourceSpan.Length > 0) {
            int writePos = _writePosition;
            Span<byte> target = _data.AsSpan(writePos);
            if (!sourceSpan.TryCopyTo(target)) {
                ReadOnlySpan<byte> src1 = sourceSpan[..target.Length];
                ReadOnlySpan<byte> src2 = sourceSpan[target.Length..];

                src1.CopyTo(target);
                src2.CopyTo(_data.AsSpan());
            }

            Interlocked.Add(ref _bytesInBuffer, sourceSpan.Length);
            _writePosition = writePos + sourceSpan.Length & _sizeMask;
        }

        return sourceSpan.Length;
    }
}
