namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Decomposes a raw 16-bit segment selector value into its table index, table indicator (GDT/LDT),
/// and requested privilege level, as defined by the Intel segment selector layout.
/// </summary>
public readonly record struct SegmentSelector {
    /// <summary>
    /// Initializes a new instance from a raw selector value.
    /// </summary>
    /// <param name="value">The raw 16-bit selector value.</param>
    public SegmentSelector(ushort value) {
        Value = value;
    }

    /// <summary>
    /// The raw 16-bit selector value.
    /// </summary>
    public ushort Value { get; }

    /// <summary>
    /// The requested privilege level (bits 0-1).
    /// </summary>
    public byte RequestedPrivilegeLevel => (byte)(Value & 0b11);

    /// <summary>
    /// Whether the selector references the LDT (bit 2 set) instead of the GDT.
    /// </summary>
    public bool ReferencesLocalDescriptorTable => (Value & 0b100) != 0;

    /// <summary>
    /// The index of the descriptor within its table (bits 3-15).
    /// </summary>
    public int Index => Value >> 3;

    /// <summary>
    /// Whether the selector is the null selector: index 0 <b>and</b> the GDT (TI=0). An LDT selector
    /// with index 0 is a normal, usable selector (it references the first LDT entry) and is not null -
    /// only a GDT index-0 selector is architecturally reserved as "null".
    /// </summary>
    public bool IsNull => Index == 0 && !ReferencesLocalDescriptorTable;

    /// <summary>
    /// The value pushed as a selector-related exception error code: the RPL bits are NOT part of the
    /// error-code layout (bit 0 EXT, bit 1 IDT, bit 2 TI, bits 3-15 index) and are always masked out,
    /// regardless of the RPL the faulting selector itself carried.
    /// </summary>
    public ushort ErrorCode => (ushort)(Value & 0xFFFC);
}
