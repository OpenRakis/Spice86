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
    public Span<byte> GetSpan(int address, int length) => _pageMemory.GetSpan(address, length);
}