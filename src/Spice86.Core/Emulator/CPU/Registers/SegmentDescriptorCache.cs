namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// The hidden descriptor cache a CPU loads into a segment register when a selector is loaded into
/// it. In protected mode this is decoded from a GDT/LDT descriptor at load time; in real/V86 mode it
/// is synthesized as <c>Base = selector * 16</c>, <c>Limit = 0xFFFF</c>. Memory accesses through the
/// segment use this cache directly, without re-reading the descriptor table on every access.
/// </summary>
public readonly record struct SegmentDescriptorCache {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="base">The 32-bit linear base address of the segment.</param>
    /// <param name="limit">The segment limit, already scaled by granularity to a byte-granular value.</param>
    /// <param name="accessRights">The raw access-rights byte (present, DPL, S, type).</param>
    /// <param name="defaultBig">Whether the segment defaults to 32-bit operands/addressing (D/B bit).</param>
    /// <param name="granularity4K">Whether the limit is scaled in 4K pages (G bit) rather than bytes.</param>
    /// <param name="present">Whether the descriptor's present bit is set.</param>
    public SegmentDescriptorCache(uint @base, uint limit, byte accessRights, bool defaultBig, bool granularity4K, bool present) {
        Base = @base;
        Limit = limit;
        AccessRights = accessRights;
        DefaultBig = defaultBig;
        Granularity4K = granularity4K;
        Present = present;
    }

    /// <summary>The 32-bit linear base address of the segment.</summary>
    public uint Base { get; }

    /// <summary>The segment limit, already scaled by granularity to a byte-granular value.</summary>
    public uint Limit { get; }

    /// <summary>The raw access-rights byte (present, DPL, S, type).</summary>
    public byte AccessRights { get; }

    /// <summary>Whether the segment defaults to 32-bit operands/addressing (D/B bit).</summary>
    public bool DefaultBig { get; }

    /// <summary>Whether the limit is scaled in 4K pages (G bit) rather than bytes.</summary>
    public bool Granularity4K { get; }

    /// <summary>Whether the descriptor's present bit is set.</summary>
    public bool Present { get; }

    /// <summary>The descriptor privilege level (access-rights byte bits 5-6).</summary>
    public byte DescriptorPrivilegeLevel => (byte)((AccessRights >> 5) & 0b11);

    /// <summary>Whether the descriptor is a code-or-data segment (S bit set) rather than a system descriptor.</summary>
    public bool IsCodeOrDataSegment => (AccessRights & 0b0001_0000) != 0;

    /// <summary>Whether a code-or-data descriptor describes executable (code) memory.</summary>
    public bool IsCode => IsCodeOrDataSegment && (AccessRights & 0b0000_1000) != 0;

    /// <summary>Whether a code descriptor is conforming (executable at any CPL &lt;= its DPL, without changing CPL).</summary>
    public bool IsConforming => IsCode && (AccessRights & 0b0000_0100) != 0;

    /// <summary>Whether a data-or-code descriptor's writable/readable bit (access byte bit 1) is set.</summary>
    public bool IsReadWriteBitSet => (AccessRights & 0b10) != 0;

    /// <summary>Creates the descriptor cache synthesized for a real-mode or V86-mode segment load.</summary>
    /// <param name="selector">The raw segment value being loaded.</param>
    public static SegmentDescriptorCache CreateRealMode(ushort selector) {
        return new SegmentDescriptorCache(
            @base: (uint)(selector << 4),
            limit: 0xFFFF,
            accessRights: 0b1001_0011, // present, DPL 0, code-or-data, read/write, accessed
            defaultBig: false,
            granularity4K: false,
            present: true);
    }
}
