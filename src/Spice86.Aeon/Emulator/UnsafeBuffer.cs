namespace Spice86.Aeon.Emulator;

using System.Runtime.CompilerServices;

/// <summary>
/// A buffer that holds an array of unmanaged elements.
/// </summary>
/// <typeparam name="T">The type of unmanaged elements stored in the buffer.</typeparam>
internal readonly struct UnsafeBuffer<T> where T : unmanaged {
    private readonly T[] array;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsafeBuffer{T}"/> class with the specified length.
    /// </summary>
    /// <param name="length">The length of the buffer to be allocated.</param>
    public UnsafeBuffer(int length) => this.array = GC.AllocateArray<T>(length, pinned: true);

    /// <summary>
    /// Returns a pointer to the first element in the buffer.
    /// </summary>
    /// <returns>A pointer to the first element in the buffer.</returns>
    public unsafe T* ToPointer() => (T*)Unsafe.AsPointer(ref this.array[0]);

    /// <summary>
    /// Clears the contents of the buffer.
    /// </summary>
    public void Clear() => Array.Clear(this.array, 0, this.array.Length);
}