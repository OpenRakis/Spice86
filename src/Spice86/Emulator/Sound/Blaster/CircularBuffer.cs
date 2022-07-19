namespace Spice86.Emulator.Sound.Blaster;

using System;
using System.Threading;

/// <summary>
/// Stores bytes of data in a circular buffer.
/// </summary>
internal sealed class CircularBuffer {
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
        this._sizeMask = capacity - 1;
        this._data = new byte[capacity];
    }

    /// <summary>
    /// Gets the size of the buffer in bytes.
    /// </summary>
    public int Capacity => this._sizeMask + 1;

    /// <summary>
    /// Reads bytes from the buffer to an array and advances the read pointer.
    /// </summary>
    /// <param name="buffer">Buffer into which bytes are written.</param>
    /// <returns>Number of bytes actually read.</returns>
    public int Read(Span<byte> buffer) {
        int bufferBytes = this._bytesInBuffer;
        int count = Math.Min(buffer.Length, bufferBytes);

        if (count > 0) {
            int readPos = this._readPosition;
            if (count <= this._data.Length - readPos) {
                Span<byte> source = this._data.AsSpan(readPos, count);
                source.CopyTo(buffer);
            } else {
                Span<byte> src1 = this._data.AsSpan(readPos, this._data.Length - readPos);
                Span<byte> src2 = this._data.AsSpan(0, count - src1.Length);
                src1.CopyTo(buffer);
                src2.CopyTo(buffer[src1.Length..]);
            }

            Interlocked.Add(ref this._bytesInBuffer, -count);
            this._readPosition = (readPos + count) & this._sizeMask;
        }

        return count;
    }
    /// <summary>
    /// Writes bytes from a location in memory to the buffer and advances the write pointer.
    /// </summary>
    /// <param name="source">Data to read.</param>
    /// <returns>Number of bytes actually written.</returns>
    public int Write(ReadOnlySpan<byte> source) {
        int bytesAvailable = this._bytesInBuffer;
        int bytesFree = this.Capacity - bytesAvailable;

        ReadOnlySpan<byte> sourceSpan = source.Length <= bytesFree ? source : source[..bytesFree];

        if (sourceSpan.Length > 0) {
            int writePos = this._writePosition;
            Span<byte> target = this._data.AsSpan(writePos);
            if (!sourceSpan.TryCopyTo(target)) {
                ReadOnlySpan<byte> src1 = sourceSpan[..target.Length];
                ReadOnlySpan<byte> src2 = sourceSpan[target.Length..];

                src1.CopyTo(target);
                src2.CopyTo(this._data.AsSpan());
            }

            Interlocked.Add(ref this._bytesInBuffer, sourceSpan.Length);
            this._writePosition = (writePos + sourceSpan.Length) & this._sizeMask;
        }

        return sourceSpan.Length;
    }
}
