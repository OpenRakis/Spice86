namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;

public class VgaRom : IMemoryDevice {
    private const int BaseAddress = Segment << 4;
    private readonly byte[] _storage;

    /// <summary>
    /// The segment of the video BIOS.
    /// </summary>
    public const int Segment = 0xC000;

    /// <summary>
    /// Creates a new instance of the <see cref="VgaRom"/> class.
    /// </summary>
    public VgaRom() {
        // Create some storage.
        byte[] font8 = Fonts.VgaFont8;
        byte[] font14 = Fonts.VgaFont14;
        byte[] font16 = Fonts.VgaFont16;

        Size = (uint)(64 + font8.Length + font14.Length + font16.Length);
        _storage = new byte[Size];

        // Populate the addresses to the fonts.
        VgaFont8Address = new SegmentedAddress(Segment, 64);
        VgaFont8Address2 = new SegmentedAddress(Segment, (ushort)(VgaFont8Address.Offset + font8.Length / 2));
        VgaFont14Address = new SegmentedAddress(Segment, (ushort)(VgaFont8Address.Offset + font8.Length));
        VgaFont16Address = new SegmentedAddress(Segment, (ushort)(VgaFont14Address.Offset + font14.Length));

        // Copy the fonts into the storage.
        font8.CopyTo(_storage, 64);
        font14.CopyTo(_storage, 64 + font8.Length);
        font16.CopyTo(_storage, 64 + font8.Length + font14.Length);
    }

    /// <summary>
    /// Gets the address of the 8x8 VGA font.
    /// </summary>
    public SegmentedAddress VgaFont8Address { get; }
    /// <summary>
    /// Gets the address of the 2nd half of the 8x8 VGA font.
    /// </summary>
    public SegmentedAddress VgaFont8Address2 { get; }
    /// <summary>
    /// Gets the address of the 8x14 VGA font.
    /// </summary>
    public SegmentedAddress VgaFont14Address { get; }
    /// <summary>
    /// Gets the address of the 8x16 VGA font.
    /// </summary>
    public SegmentedAddress VgaFont16Address { get; }

    /// <inheritdoc />
    public uint Size { get; }

    /// <inheritdoc />
    public byte Read(uint address) {
        return _storage[address - BaseAddress];
    }

    /// <inheritdoc />
    public void Write(uint address, byte value) {
        throw new NotSupportedException($"Video BIOS ROM is read-only. Writing {value:X2} to {address:X6} is not supported.");
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int address, int length) {
        return _storage.AsSpan(address - BaseAddress, length);
    }
}