namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// A system segment register (LDTR or TR): a selector into the GDT plus the descriptor cache loaded
/// for it, loaded by LLDT/LTR and read back by SLDT/STR.
/// </summary>
public class SystemSegmentRegister {
    /// <summary>The selector currently loaded into the register.</summary>
    public ushort Selector { get; set; }

    /// <summary>The descriptor cache loaded for <see cref="Selector"/>.</summary>
    public SegmentDescriptorCache DescriptorCache { get; set; }
}
