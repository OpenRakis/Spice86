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
    void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind);

    /// <summary>
    /// Translates a segmented byte lane to a physical address.
    /// </summary>
    /// <param name="segment">The segment selector or real-mode segment value.</param>
    /// <param name="offset">The byte-lane offset.</param>
    /// <returns>The translated physical address.</returns>
    uint TranslateAddress(ushort segment, uint offset);
}