namespace Spice86.Core;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>A fast stack implementation with optional span-based backing storage.</summary>
/// <typeparam name="T">The stack element type.</typeparam>
internal ref partial struct ValueListStack<T> {
    private ValueListBuilder<T> _list;

    /// <summary>Initializes a new stack with a user-supplied scratch buffer.</summary>
    /// <param name="scratchBuffer">A span containing the initial element storage for the stack.</param>
    /// <remarks>
    /// This is typically a <see langword="stackalloc"/> allocated buffer from the caller's scope. The stack will still
    /// be empty after calling this method. It is still up to the caller to explicitly push elements on to the empty
    /// stack after initialization.
    /// </remarks>
    public ValueListStack(Span<T?> scratchBuffer) => _list = new(scratchBuffer);

    /// <summary>Initializes a new stack with a specified initial capacity.</summary>
    /// <param name="capacity">The initial minimum capacity to allocate. Must be greater than or equal to zero.</param>
    /// <remarks>
    /// The internal storage will use the default <see cref="System.Buffers.ArrayPool{T}"/> for heap allocations.
    /// </remarks>
    public ValueListStack(int capacity) => _list = new(capacity);

    /// <summary>Gets or sets the length of the stack in elements.</summary>
    /// <value>The length of the stack in elements. Must be greater than or equal to zero.</value>
    /// <remarks>
    /// Changing the stack length will add or remove elements, but they will not be initialized or cleared. That is a
    /// task that is up to the caller to perform.
    /// </remarks>
    public int Length {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _list.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _list.Length = value;
    }

    /// <summary>Gets a reference to the element at the specified index.</summary>
    /// <param name="index">
    /// Zero-based index to the requested stack element. Must be greater than or equal to zero and less than
    /// <see cref="Length"/>. Index zero represents the first element added to the stack, one represents the second
    /// element, etc. The last index is always the last element added to the stack.
    /// </param>
    /// <returns>A reference to the specified element.</returns>
    public ref T this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _list[index];
    }

    /// <summary>Gets the elements on the stack as a read only span.</summary>
    /// <returns>A read only span containing the current elements on the stack.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<T> AsSpan() => _list.AsSpan();

    /// <summary>Disposes of the internal stack memory.</summary>
    /// <remarks>
    /// Calling this after the stack is no longer needed is absolutely essential, otherwise memory leaks may occur!
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => _list.Dispose();

    /// <summary>Pushes a single element on to the end of the stack.</summary>
    /// <param name="item">The element to copy into the stack.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(T item) => _list.Append(item);

    /// <summary>Pushes an entire span of elements on to the end of the stack.</summary>
    /// <param name="source">
    /// The elements to copy into the stack. Must be specified in the same order as returned by <see cref="AsSpan()"/>.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushMany(scoped ReadOnlySpan<T> source) => _list.Append(source);

    /// <summary>Pushes an uninitialized element on to the end of the stack.</summary>
    /// <returns>A reference to the uninitialized element.</returns>
    /// <remarks>
    /// This is a potentially dangerous operation. It is up to the caller to initialize the element. This is useful for
    /// performing zero-copy initialization on large structure elements.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T PushReserve() => ref _list.AppendSpan(1)[0];

    /// <summary>Pushes multiple uninitialized elements on to the end of the stack.</summary>
    /// <param name="length">The number of elements to push. Must be greater than or equal to zero.</param>
    /// <returns>A span containing the uninitialized elements that were pushed.</returns>
    /// <remarks>
    /// This is a potentially dangerous operation. It is up to the caller to initialize all the elements.. This is
    /// useful for performing zero-copy initialization on large structure elements.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> PushReserveMany(int length) => _list.AppendSpan(length);

    /// <summary>Attempts to pop a single element from the stack.</summary>
    /// <param name="item">
    /// The item that was popped from the stack or <see langword="default"/> if the stack is empty.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an element was successfully popped from the stack; otherwise, <see cref="false"/>.
    /// </returns>
    public bool TryPop([MaybeNullWhen(false)] out T item) {
        if (Length <= 0) {
            item = default;
            return false;
        }

        // Get a reference to the last item and copy it into the output parameter.
        int lastElementIndex = _list.Length - 1;
        ref T lastItem = ref _list[lastElementIndex];
        item = lastItem;

        // Clear the element from the underlying list if the backing storage is in an array. (This is what
        // ValueListBuilder<T> does when calling Dispose() or when it grows. Since ValueListBuilder<T> does not have a
        // concept of removing items explicitly, it is technically up to the caller: this method.)
        if (_list.IsArrayBacked && !typeof(T).IsPrimitive) {
            lastItem = default!;
        }

        // Update stack length to exclude the popped element.
        _list.Length = lastElementIndex;
        return true;
    }

    /// <summary>Clears all elements in the stack.</summary>
    public void Clear() {
        // Clear all elements from the underlying list if the backing storage is in an array. (This is what
        // ValueListBuilder<T> does when calling Dispose() or when it grows. Since ValueListBuilder<T> does not have a
        // concept of removing items explicitly, it is technically up to the caller: this method.)
        // The length check isn't strictly necessary here due to how ValueListBuilder works, but it provides extra
        // safety for a negligible cost.
        if (_list.IsArrayBacked && !typeof(T).IsPrimitive && _list.Length > 0) {
            MemoryMarshal.CreateSpan(ref _list[0], _list.Length).Clear();
        }

        _list.Length = 0;
    }
}
