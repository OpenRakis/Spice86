namespace Spice86.Core.Emulator.Function;

using System.Text;

/// <summary>
/// <para>Stores a fixed number of items in a circular buffer.</para>
/// <para>
/// Useful for keeping track of the last X items in a sequence.
/// It "forgets" the oldest item when the buffer is full and just keeps going.
/// </para>
/// </summary>
/// <typeparam name="T">The type of items to store</typeparam>
public class CircularBuffer<T> {
    private readonly T[] _buffer;
    private int _writeIndex;

    /// <summary>
    /// Initializes a new instance of the CircularBuffer class.
    /// </summary>
    /// <param name="capacity">The fixed capacity of the buffer.</param>
    public CircularBuffer(int capacity) {
        _buffer = new T[capacity];
    }

    /// <summary>
    /// Adds a value to the buffer.
    /// </summary>
    /// <param name="value"></param>
    public void Add(T value) {
        _buffer[_writeIndex] = value;
        _writeIndex = (_writeIndex + 1) % _buffer.Length;
    }

    /// <summary>
    /// Dumps the buffer to a string.
    /// </summary>
    /// <returns></returns>
    public override string ToString() {
        var sb = new StringBuilder();
        int bufferLength = _buffer.Length;
        for (int i = _writeIndex; i < _writeIndex + bufferLength; i++) {
            int bufferIndex = i % bufferLength;
            sb.AppendLine(_buffer[bufferIndex]?.ToString());
        }

        return sb.ToString();
    }
}