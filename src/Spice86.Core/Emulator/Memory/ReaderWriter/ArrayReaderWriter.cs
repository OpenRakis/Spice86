namespace Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Diagnostics;
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
    public bool TryGetSpan(out uint startAddress, out Span<T> span, MemoryAccess access) {
        startAddress = 0;
        span = Array;
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(out uint startAddress, out ReadOnlySpan<T> span, MemoryAccess access) {
        startAddress = 0;
        span = Array;
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out Span<T> span, MemoryAccess access) {
        T[] array = Array;
        long lengthRemaining = array.Length - startAddress;
        if (lengthRemaining >= 0) {
            // Cast from long to int is safe because length remaining is in the range 0..array.Length and guaranteed
            // that adding the start address to it will not go out of bounds of array.
            span = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), startAddress), (int)lengthRemaining);
            return true;
        }

        span = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out ReadOnlySpan<T> span, MemoryAccess access) {
        T[] array = Array;
        long lengthRemaining = array.Length - startAddress;
        if (lengthRemaining >= 0) {
            // Cast from long to int is safe because length remaining is in the range 0..array.Length and guaranteed
            // that adding the start address to it will not go out of bounds of array.
            span = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), startAddress), (int)lengthRemaining);
            return true;
        }

        span = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out Span<T> span, MemoryAccess access) {
        T[] array = Array;
        long lengthRemaining = array.Length - startAddress;
        if (lengthRemaining >= (uint)length) {
            // CreateSpan is safe because of above length check (length will always be in the range 0..array.Length and
            // guaranteed that adding the start address to it will not go out of bounds of array).
            Debug.Assert((uint)length <= (uint)array.Length);
            span = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), startAddress), length);
            return true;
        }

        span = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out ReadOnlySpan<T> span, MemoryAccess access) {
        T[] array = Array;
        long lengthRemaining = array.Length - startAddress;
        if (lengthRemaining >= (uint)length) {
            // CreateSpan is safe because of above length check (length will always be in the range 0..array.Length and
            // guaranteed that adding the start address to it will not go out of bounds of array).
            Debug.Assert((uint)length <= (uint)array.Length);
            span = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), startAddress), length);
            return true;
        }

        span = [];
        return false;
    }
}
