namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Return codes for CP/M-style FCB operations, aligned with FreeDOS <c>fcbfns.c</c> semantics.
/// </summary>
public enum FcbStatus : byte {
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Success = 0x00,

    /// <summary>
    /// No data was read or written (EOF or disk full depending on the operation).
    /// </summary>
    NoData = 0x01,

    /// <summary>
    /// Requested record range would wrap past the 64KiB segment boundary.
    /// </summary>
    SegmentWrap = 0x02,

    /// <summary>
    /// End of file reached after a partial record transfer.
    /// </summary>
    EndOfFile = 0x03,

    /// <summary>
    /// Generic failure (file not found, invalid handle, or I/O error).
    /// </summary>
    Error = 0xFF
}
