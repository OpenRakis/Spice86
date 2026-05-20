namespace Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Interface for objects that allow to read at specific addresses
/// </summary>
public interface IReaderWriter<T> {
    /// <summary>
    /// Provides read / write access at address
    /// </summary>
    /// <param name="address">Address where to perform the operation</param>
    public T this[uint address] { get; set; }

    /// <summary>
    /// Length of the address space
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Attempts to get a span from the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address for the returned span.</param>
    /// <param name="span">A span containing all the elements in the requested memory.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// When <paramref name="access"/> is set to <see cref="MemoryAccess.None"/> (the default), then typically no
    /// debugger breakpoints will be checked prior to retrieving the data.
    /// </remarks>
    public virtual bool TryGetSpan(out uint startAddress, out Span<T> span, MemoryAccess access = MemoryAccess.None) {
        startAddress = 0;
        return TryGetSpan(0, out span);
    }

    /// <summary>
    /// Attempts to get a read only span from the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address for the returned span.</param>
    /// <param name="span">A read only span containing all the elements in the requested memory.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// When <paramref name="access"/> is set to <see cref="MemoryAccess.None"/> (the default), then typically no
    /// debugger breakpoints will be checked prior to retrieving the data.
    /// </remarks>
    public virtual bool TryGetSpan(out uint startAddress, out ReadOnlySpan<T> span, MemoryAccess access = MemoryAccess.None) {
        bool result = TryGetSpan(out startAddress, out Span<T> mutableSpan, access);
        span = mutableSpan;
        return result;
    }

    /// <summary>
    /// Attempts to get a span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request elements from.</param>
    /// <param name="span">A span containing the remaining elements starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// When <paramref name="access"/> is set to <see cref="MemoryAccess.None"/> (the default), then typically no
    /// debugger breakpoints will be checked prior to retrieving the data.
    /// </remarks>
    public virtual bool TryGetSpan(uint startAddress, out Span<T> span, MemoryAccess access = MemoryAccess.None) {
        span = [];
        return false;
    }

    /// <summary>
    /// Attempts to get a read only span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request elements from.</param>
    /// <param name="span">A span containing the remaining elements starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// When <paramref name="access"/> is set to <see cref="MemoryAccess.None"/> (the default), then typically no
    /// debugger breakpoints will be checked prior to retrieving the data.
    /// </remarks>
    public virtual bool TryGetSpan(uint startAddress, out ReadOnlySpan<T> span, MemoryAccess access = MemoryAccess.None) {
        bool result = TryGetSpan(startAddress, out Span<T> mutableSpan, access);
        span = mutableSpan;
        return result;
    }

    /// <summary>
    /// Attempts to get a span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request elements from.</param>
    /// <param name="length">The number of elements requested.</param>
    /// <param name="span">A span containing the number of requested elements starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// Implementors should always return a span with <paramref name="length"/> elements if successful. Callers should
    /// only assume that all implementors will return a span with <em>at least</em> <paramref name="length"/> elements
    /// on success.
    /// </para>
    /// <para>
    /// When <paramref name="access"/> is set to <see cref="MemoryAccess.None"/> (the default), then typically no
    /// debugger breakpoints will be checked prior to retrieving the data.
    /// </para>
    /// </remarks>
    public virtual bool TryGetSpan(uint startAddress, int length, out Span<T> span, MemoryAccess access = MemoryAccess.None) {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        span = [];
        return false;
    }

    /// <summary>
    /// Attempts to get a read only span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request elements from.</param>
    /// <param name="length">The number of elements requested.</param>
    /// <param name="span">A span containing the number of requested elements starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// Implementors should always return a span with <paramref name="length"/> elements if successful. Callers should
    /// only assume that all implementors will return a span with <em>at least</em> <paramref name="length"/> elements
    /// on success.
    /// </para>
    /// <para>
    /// When <paramref name="access"/> is set to <see cref="MemoryAccess.None"/> (the default), then typically no
    /// debugger breakpoints will be checked prior to retrieving the data.
    /// </para>
    /// </remarks>
    public virtual bool TryGetSpan(uint startAddress, int length, out ReadOnlySpan<T> span, MemoryAccess access = MemoryAccess.None) {
        bool result = TryGetSpan(startAddress, length, out Span<T> mutableSpan, access);
        span = mutableSpan;
        return result;
    }
}
