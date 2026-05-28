namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// A representation of a logical EMM page.
/// </summary>
public class EmmPage : IMemoryDevice {
    private readonly Ram _pageMemory;
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="size">The size of the emm page, in bytes.</param>
    public EmmPage(uint size) {
        _pageMemory = new Ram(size);
        Size = _pageMemory.Size;
    }

    /// <summary>
    /// The logical page number, for book keeping inside our dictionaries.
    /// </summary>
    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;

    /// <inheritdoc />
    public uint Size { get; }

    /// <inheritdoc />
    public byte Read(uint address) => _pageMemory.Read(address);

    /// <inheritdoc />
    public void Write(uint address, byte value) => _pageMemory.Write(address, value);

    /// <inheritdoc />
    public IList<byte> GetSlice(int address, int length) => _pageMemory.GetSlice(address, length);

    /// <inheritdoc/>
    public bool TryGetSpan(out uint startAddress, out Span<byte> span, MemoryAccess access)
        => _pageMemory.TryGetSpan(out startAddress, out span, access);

    /// <inheritdoc/>
    public bool TryGetSpan(out uint startAddress, out ReadOnlySpan<byte> span, MemoryAccess access)
        => _pageMemory.TryGetSpan(out startAddress, out span, access);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out Span<byte> span, MemoryAccess access)
        => _pageMemory.TryGetSpan(startAddress, out span, access);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, out ReadOnlySpan<byte> span, MemoryAccess access)
        => _pageMemory.TryGetSpan(startAddress, out span, access);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out Span<byte> span, MemoryAccess access)
        => _pageMemory.TryGetSpan(startAddress, length, out span, access);

    /// <inheritdoc/>
    public bool TryGetSpan(uint startAddress, int length, out ReadOnlySpan<byte> span, MemoryAccess access)
        => _pageMemory.TryGetSpan(startAddress, length, out span, access);
}