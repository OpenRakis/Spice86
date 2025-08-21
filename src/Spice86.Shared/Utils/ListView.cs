namespace Spice86.Shared.Utils;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Represents a windowed view into an existing <see cref="IList{T}"/> without copying data.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class ListView<T> : IList<T> {
    private readonly IList<T> _source;
    private readonly int _offset;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListView{T}"/> class.
    /// </summary>
    /// <param name="source">The source list to wrap.</param>
    /// <param name="offset">The starting index of the slice within the source list.</param>
    /// <param name="count">The number of elements in the slice.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="offset"/> or <paramref name="count"/> is negative,
    /// or if <paramref name="offset"/> + <paramref name="count"/> exceeds the source list's count.
    /// </exception>
    public ListView(IList<T> source, int offset, int count) {
        if (source == null) {
            throw new ArgumentNullException(nameof(source));
        }

        if (offset < 0 || count < 0 || offset + count > source.Count) {
            throw new ArgumentOutOfRangeException();
        }

        if (source is ListView<T> sourceListSlice) {
            // Avoid nesting
            _source = sourceListSlice._source;
            _offset = sourceListSlice._offset + offset;
            Count = count;
        } else {
            _source = source;
            _offset = offset;
            Count = count;
        }
    }

    /// <inheritdoc />
    public int Count { get; }

    /// <inheritdoc />
    public T this[int index] {
        get => _source[_offset + index];
        set => _source[_offset + index] = value;
    }

    /// <inheritdoc />
    public bool IsReadOnly => _source.IsReadOnly;

    /// <summary>
    /// Not supported. A slice has a fixed size and cannot be extended.
    /// </summary>
    /// <inheritdoc />
    public void Add(T item) => throw new NotSupportedException("Cannot add to a fixed-size slice.");

    /// <summary>
    /// Not supported. A slice cannot be cleared independently of its source.
    /// </summary>
    /// <inheritdoc />
    public void Clear() => throw new NotSupportedException("Cannot clear a fixed-size slice.");

    /// <inheritdoc />
    public bool Contains(T item) {
        return IndexOf(item) != -1;
    }

    /// <inheritdoc />
    public void CopyTo(T[] array, int arrayIndex) {
        for (int i = 0; i < Count; i++) {
            array[arrayIndex + i] = _source[_offset + i];
        }
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() {
        for (int i = 0; i < Count; i++) {
            yield return _source[_offset + i];
        }
    }

    /// <inheritdoc />
    public int IndexOf(T item) {
        for (int i = 0; i < Count; i++) {
            if (EqualityComparer<T>.Default.Equals(_source[_offset + i], item)) {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Not supported. A slice has a fixed size and cannot be extended.
    /// </summary>
    /// <inheritdoc />
    public void Insert(int index, T item) => throw new NotSupportedException("Cannot insert into a fixed-size slice.");

    /// <summary>
    /// Not supported. A slice has a fixed size and cannot remove elements.
    /// </summary>
    /// <inheritdoc />
    public bool Remove(T item) => throw new NotSupportedException("Cannot remove from a fixed-size slice.");

    /// <summary>
    /// Not supported. A slice has a fixed size and cannot remove elements.
    /// </summary>
    /// <inheritdoc />
    public void RemoveAt(int index) => throw new NotSupportedException("Cannot remove from a fixed-size slice.");

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}