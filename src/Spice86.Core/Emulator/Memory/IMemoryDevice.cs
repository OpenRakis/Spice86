namespace Spice86.Core.Emulator.Memory;

/// <summary>
///     Represents any device that can be mapped to memory.
/// </summary>
public interface IMemoryDevice {
    /// <summary>
    ///     The size of the device in bytes.
    /// </summary>
    uint Size {
        get;
    }

    /// <summary>
    ///     Read a byte from the device.
    /// </summary>
    /// <param name="address">The memory address to read from</param>
    /// <returns>The byte value stored at the specified location</returns>
    byte Read(uint address);

    /// <summary>
    ///     Write a byte to the device.
    /// </summary>
    /// <param name="address">The memory address to write to</param>
    /// <param name="value">The byte value to write</param>
    void Write(uint address, byte value);

    /// <summary>
    /// Get a list of bytes from the device.
    /// </summary>
    /// <param name="address">The start address of the memory</param>
    /// <param name="length">The length of the slice</param>
    /// <returns>A byte list</returns>
    public IList<byte> GetSlice(int address, int length);

    /// <summary>
    /// Attempts to get a span from the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address for the returned span.</param>
    /// <param name="span">A span containing all the bytes in the requested memory.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    public virtual bool TryGetSpan(out uint startAddress, out Span<byte> span, MemoryAccess access) {
        startAddress = 0;
        if ((int)Size >= 0) {
            return TryGetSpan(0, (int)Size, out span, access);
        }

        span = [];
        return false;
    }

    /// <summary>
    /// Attempts to get a read only span from the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address for the returned span.</param>
    /// <param name="span">A read only span containing all the bytes in the requested memory.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    public virtual bool TryGetSpan(out uint startAddress, out ReadOnlySpan<byte> span, MemoryAccess access) {
        bool result = TryGetSpan(out startAddress, out Span<byte> mutableSpan, access);
        span = mutableSpan;
        return result;
    }

    /// <summary>
    /// Attempts to get a span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request bytes from.</param>
    /// <param name="span">A span containing the remaining bytes starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    public virtual bool TryGetSpan(uint startAddress, out Span<byte> span, MemoryAccess access) {
        if ((long)Size - startAddress is >= 0 and <= int.MaxValue) {
            return TryGetSpan(startAddress, (int)(Size - startAddress), out span, access);
        }

        span = [];
        return false;
    }

    /// <summary>
    /// Attempts to get a read only span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request bytes from.</param>
    /// <param name="span">A span containing the remaining bytes starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    public virtual bool TryGetSpan(uint startAddress, out ReadOnlySpan<byte> span, MemoryAccess access) {
        bool result = TryGetSpan(startAddress, out Span<byte> mutableSpan, access);
        span = mutableSpan;
        return result;
    }

    /// <summary>
    /// Attempts to get a span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request bytes from.</param>
    /// <param name="length">The number of bytes requested. A negative length should always result in a failure.</param>
    /// <param name="span">A span containing the number of requested bytes starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Implementors should always return a span with <paramref name="length"/> bytes if successful. Callers should
    /// only assume that all implementors will return a span with <em>at least</em> <paramref name="length"/> bytes
    /// on success.
    /// </remarks>
    public virtual bool TryGetSpan(uint startAddress, int length, out Span<byte> span, MemoryAccess access) {
        span = [];
        return false;
    }

    /// <summary>
    /// Attempts to get a read only span from a portion of the reader/writer.
    /// </summary>
    /// <param name="startAddress">The starting address to request bytes from.</param>
    /// <param name="length">The number of bytes requested. A negative length should always result in a failure.</param>
    /// <param name="span">A span containing the number of requested bytes starting at the given address.</param>
    /// <param name="access">Specifies the type of memory access that is requested for the span.</param>
    /// <returns><see langword="true"/> if the span was successfully retrieved; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Implementors should always return a span with <paramref name="length"/> bytes if successful. Callers should
    /// only assume that all implementors will return a span with <em>at least</em> <paramref name="length"/> bytes
    /// on success.
    /// </remarks>
    public virtual bool TryGetSpan(uint startAddress, int length, out ReadOnlySpan<byte> span, MemoryAccess access) {
        bool result = TryGetSpan(startAddress, length, out Span<byte> mutableSpan, access);
        span = mutableSpan;
        return result;
    }
}