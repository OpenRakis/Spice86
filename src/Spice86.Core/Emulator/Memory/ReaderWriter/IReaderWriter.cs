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
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    public virtual bool TryGetSpan(out uint startAddress, out Span<T> span) {
        startAddress = 0;
        return TryGetSpan(0, out span);
    }

    /// <summary>
    /// Attempts to get a span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request elements from.</param>
    /// <param name="span">A span containing the remaining elements starting at the given address.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    public virtual bool TryGetSpan(uint startAddress, out Span<T> span) {
        span = [];
        return false;
    }

    /// <summary>
    /// Attempts to get a span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request elements from.</param>
    /// <param name="length">The number of elements requested.</param>
    /// <param name="span">A span containing the number of requested elements starting at the given address.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <remarks>
    /// Implementors should always return a span with <paramref name="length"/> elements if successful.
    /// </remarks>
    public virtual bool TryGetSpan(uint startAddress, int length, out Span<T> span) {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        span = [];
        return false;
    }
}
