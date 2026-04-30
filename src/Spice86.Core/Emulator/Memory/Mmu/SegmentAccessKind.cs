namespace Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// Identifies the semantic class of a segmented memory access.
/// </summary>
public enum SegmentAccessKind {
    /// <summary>
    /// Ordinary data memory access, which maps real-mode limit violations to #GP.
    /// </summary>
    Data,

    /// <summary>
    /// Stack memory access, which maps real-mode limit violations to #SS.
    /// </summary>
    Stack
}