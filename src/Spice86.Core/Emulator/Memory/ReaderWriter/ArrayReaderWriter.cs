namespace Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Implementation of IReaderWriter on top of an array of type T
/// </summary>
/// <typeparam name="T"></typeparam>
public class ArrayReaderWriter<T> : IReaderWriter<T> {
    public T[] Array { get; }

    public ArrayReaderWriter(T[] array) {
        Array = array;
    }

    /// <inheritdoc/>
    public T this[uint address] {
        get => Array[address];
        set => Array[address] = value;
    }

    /// <inheritdoc/>
    public int Length { get => Array.Length; }

    /// <inheritdoc/>
    public bool TryGetSpan(out uint startAddress, out Span<T> span) {
        startAddress = 0;
        span = Array;
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out Span<T> span) {
        T[] array = Array;
        long lengthRemaining = array.Length - startAddress;
        if (lengthRemaining >= 0) {
            // Cast from long to int is safe because length remaining is in the range 0..array.Length (inclusive).
            span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), startAddress), (int)lengthRemaining);
            return true;
        }

        span = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out Span<T> span) {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        T[] array = Array;
        long lengthRemaining = array.Length - startAddress;
        if (lengthRemaining >= length) {
            // Cast from long to int is safe because length remaining is in the range 0..array.Length (inclusive).
            span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), startAddress), (int)lengthRemaining);
            return true;
        }

        span = [];
        return false;
    }
}
