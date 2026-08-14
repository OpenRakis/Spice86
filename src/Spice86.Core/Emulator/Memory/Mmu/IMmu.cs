namespace Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// Translates segmented memory accesses and validates their segment-level limits.
/// </summary>
public interface IMmu {
    /// <summary>
    /// Checks whether a segmented access is valid for the current memory-management policy and throws
    /// the appropriate CPU exception if not.
    /// </summary>
    /// <param name="segment">The segment selector or real-mode segment value.</param>
    /// <param name="offset">The effective offset before any truncation.</param>
    /// <param name="length">The access length in bytes.</param>
    /// <param name="accessKind">The semantic access kind.</param>
    /// <param name="isWrite">Whether the access is a write; a write to a non-writable data segment raises #GP.</param>
    void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind, bool isWrite);

    /// <summary>
    /// Translates a segmented byte lane to a physical address.
    /// </summary>
    /// <param name="segment">The segment selector or real-mode segment value.</param>
    /// <param name="offset">The byte-lane offset.</param>
    /// <param name="isWrite">Whether this lane is being written rather than read; used by paging to set the PTE Dirty bit and to enforce the Read/Write protection bit.</param>
    /// <returns>The translated physical address.</returns>
    uint TranslateAddress(ushort segment, uint offset, bool isWrite);

    /// <summary>
    /// Translates an already-computed linear address (not a segment:offset pair) to a physical address:
    /// used for GDT/LDT/IDT/TSS accesses, whose base addresses are linear rather than segment-relative.
    /// Applies paging when it is enabled; a no-op otherwise. Every <see cref="IMmu"/> implementation
    /// other than <see cref="Mmu.PagingMmu"/> returns <paramref name="linearAddress"/> unchanged, since
    /// paging is applied only by the outermost wrapper in the MMU chain.
    /// </summary>
    /// <param name="linearAddress">The linear address to translate.</param>
    /// <param name="isWrite">Whether this address is being written rather than read; used by paging to set the PTE Dirty bit and to enforce the Read/Write protection bit.</param>
    /// <returns>The translated physical address.</returns>
    uint TranslateLinearAddress(uint linearAddress, bool isWrite);
}