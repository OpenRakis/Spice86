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

    /// <summary>
    /// Attempts to translate a range of segmented addresses to a sequential range of physical addresses.
    /// </summary>
    /// <param name="segment">The segment selector or real-mode segment value.</param>
    /// <param name="offset">The byte-lane offset.</param>
    /// <param name="length">The address range length in bytes.</param>
    /// <param name="startAddress">The first address in the range of translated physical addresses.</param>
    /// <returns><see langword="true"/> if all translated addresses are valid and sequentially ordered; otherwise, <see langword="false"/>.</returns>
    virtual bool TryTranslateAddressRange(ushort segment, uint offset, uint length, out uint startAddress) {
        // Make sure range will not result in a 32-bit unsigned arithmetic overflow on offset.
        if ((ulong)offset + length > uint.MaxValue) {
            startAddress = 0;
            return false;
        }

        // Make sure all translated 32-bit addresses in the range are ordered sequentially.
        uint baseAddress = TranslateAddress(segment, offset);
        for (uint i = 1; i < length; i++) {
            uint elementAddress = TranslateAddress(segment, offset + i);
            if (elementAddress - baseAddress != i) {
                startAddress = 0;
                return false;
            }
        }

        startAddress = baseAddress;
        return true;
    }
}
