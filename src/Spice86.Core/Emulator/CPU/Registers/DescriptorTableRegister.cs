namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// A descriptor table register (GDTR or IDTR): a linear base address and a byte limit, loaded by
/// LGDT/LIDT and read back by SGDT/SIDT.
/// </summary>
public class DescriptorTableRegister {
    /// <summary>The 32-bit linear base address of the table.</summary>
    public uint Base { get; set; }

    /// <summary>The byte limit of the table (table size in bytes, minus one).</summary>
    public ushort Limit { get; set; }
}
