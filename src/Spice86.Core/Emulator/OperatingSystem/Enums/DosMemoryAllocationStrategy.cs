namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Defines the memory allocation strategy used by DOS for INT 21h/48h (Allocate Memory).
/// Set/Get via INT 21h/58h.
/// </summary>
/// <remarks>
/// The strategy determines how DOS searches for a free memory block to satisfy an allocation request.
/// These values match those used by MS-DOS and FreeDOS.
/// </remarks>
public enum DosMemoryAllocationStrategy : byte {
    /// <summary>
    /// First fit: Allocate from the first block that is large enough.
    /// This is the fastest strategy but may lead to fragmentation.
    /// </summary>
    FirstFit = 0x00,

    /// <summary>
    /// Best fit: Allocate from the smallest block that is large enough.
    /// This minimizes wasted space but may be slower.
    /// </summary>
    BestFit = 0x01,

    /// <summary>
    /// Last fit: Allocate from the last (highest address) block that is large enough.
    /// This keeps low memory free for TSRs and drivers.
    /// </summary>
    LastFit = 0x02,

    /// <summary>
    /// First fit, try high memory first, then low.
    /// Used when UMBs are linked to the MCB chain.
    /// </summary>
    FirstFitHighThenLow = 0x40,

    /// <summary>
    /// Best fit, try high memory first, then low.
    /// Used when UMBs are linked to the MCB chain.
    /// </summary>
    BestFitHighThenLow = 0x41,

    /// <summary>
    /// Last fit, try high memory first, then low.
    /// Used when UMBs are linked to the MCB chain.
    /// </summary>
    LastFitHighThenLow = 0x42,

    /// <summary>
    /// First fit, high memory only (no fallback to low).
    /// Used when UMBs are linked to the MCB chain.
    /// </summary>
    FirstFitHighOnlyNoFallback = 0x80,

    /// <summary>
    /// Best fit, high memory only (no fallback to low).
    /// Used when UMBs are linked to the MCB chain.
    /// </summary>
    BestFitHighOnlyNoFallback = 0x81,

    /// <summary>
    /// Last fit, high memory only (no fallback to low).
    /// Used when UMBs are linked to the MCB chain.
    /// </summary>
    LastFitHighOnlyNoFallback = 0x82
}
