namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// The raw 8-byte layout of a GDT/LDT segment descriptor, decoded from memory. Distinct from
/// <see cref="SegmentDescriptorCache"/>, which is what a CPU segment register caches once a
/// descriptor has been read and validated by a segment load.
/// </summary>
public readonly struct RawSegmentDescriptor {
    /// <summary>
    /// Decodes a segment descriptor from its raw 8-byte in-memory representation.
    /// </summary>
    /// <param name="descriptorBytes">The 8 raw descriptor bytes, in the order they appear in memory.</param>
    public RawSegmentDescriptor(ReadOnlySpan<byte> descriptorBytes) {
        if (descriptorBytes.Length != 8) {
            throw new ArgumentException("A segment descriptor is exactly 8 bytes.", nameof(descriptorBytes));
        }

        ushort limitLow = (ushort)(descriptorBytes[0] | (descriptorBytes[1] << 8));
        uint baseLow = (uint)(descriptorBytes[2] | (descriptorBytes[3] << 8) | (descriptorBytes[4] << 16));
        byte limitHighAndFlags = descriptorBytes[6];
        byte baseHigh = descriptorBytes[7];

        AccessByte = descriptorBytes[5];
        Available = (limitHighAndFlags & 0x10) != 0;
        DefaultBig = (limitHighAndFlags & 0x40) != 0;
        Granularity4K = (limitHighAndFlags & 0x80) != 0;

        byte limitHigh = (byte)(limitHighAndFlags & 0x0F);
        uint rawLimit = (uint)((limitHigh << 16) | limitLow);
        Limit = Granularity4K ? (rawLimit << 12) | 0xFFF : rawLimit;
        Base = baseLow | ((uint)baseHigh << 24);
    }

    /// <summary>The 32-bit linear base address encoded in the descriptor.</summary>
    public uint Base { get; }

    /// <summary>The segment limit, already scaled to a byte value when granularity is 4K pages.</summary>
    public uint Limit { get; }

    /// <summary>The raw access byte (present, DPL, S, type).</summary>
    public byte AccessByte { get; }

    /// <summary>Whether the AVL (software-available) bit is set.</summary>
    public bool Available { get; }

    /// <summary>Whether the segment defaults to 32-bit operands/addressing (D/B bit).</summary>
    public bool DefaultBig { get; }

    /// <summary>Whether the limit is scaled in 4K pages (G bit) rather than bytes.</summary>
    public bool Granularity4K { get; }

    /// <summary>Whether the descriptor's present bit (access byte bit 7) is set.</summary>
    public bool Present => (AccessByte & 0x80) != 0;

    /// <summary>The descriptor privilege level (access byte bits 5-6).</summary>
    public byte DescriptorPrivilegeLevel => (byte)((AccessByte >> 5) & 0b11);

    /// <summary>Whether this is a code-or-data descriptor (S bit set) rather than a system descriptor.</summary>
    public bool IsCodeOrDataSegment => (AccessByte & 0x10) != 0;

    /// <summary>The type field (access byte bits 0-3). Meaning depends on <see cref="IsCodeOrDataSegment"/>.</summary>
    public byte Type => (byte)(AccessByte & 0x0F);

    /// <summary>Converts this raw descriptor into the cache a segment register loads it into.</summary>
    public SegmentDescriptorCache ToSegmentDescriptorCache() {
        return new SegmentDescriptorCache(Base, Limit, AccessByte, DefaultBig, Granularity4K, Present);
    }
}
