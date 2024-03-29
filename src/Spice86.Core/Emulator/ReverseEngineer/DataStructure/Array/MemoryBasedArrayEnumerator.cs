﻿namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using System.Collections;

/// <summary>
/// Enumerator for Memory based arrays
/// </summary>
public sealed class MemoryBasedArrayEnumerator<T> : IEnumerator<T> {
    private readonly MemoryBasedArray<T> _memoryBasedArray;
    private int _position = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryBasedArrayEnumerator{T}"/> class.
    /// </summary>
    /// <param name="memoryBasedArray">The memory array used for the enumeration.</param>
    public MemoryBasedArrayEnumerator(MemoryBasedArray<T> memoryBasedArray) {
        _memoryBasedArray = memoryBasedArray;
    }

    /// <inheritdoc/>
    public bool MoveNext() {
        _position++;
        return _position < _memoryBasedArray.Count;
    }

    /// <inheritdoc/>
    public void Reset() {
        _position = -1;
    }

    /// <inheritdoc/>
    public T Current { get => _memoryBasedArray[_position]; }

    /// <inheritdoc/>
    object? IEnumerator.Current => Current;

    /// <inheritdoc/>
    public void Dispose() {
        // Nothing to dispose
    }
}