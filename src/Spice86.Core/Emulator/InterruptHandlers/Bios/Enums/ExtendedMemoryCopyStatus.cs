namespace Spice86.Core.Emulator.InterruptHandlers.Bios.Enums;

/// <summary>
/// Status codes returned by INT 15h, AH=87h (Copy Extended Memory) function.
/// These values are returned in the AH register.
/// </summary>
public enum ExtendedMemoryCopyStatus : byte {
    /// <summary>
    /// Operation completed successfully - source copied into destination.
    /// </summary>
    SourceCopiedIntoDest = 0x00,

    /// <summary>
    /// RAM parity error detected during copy operation.
    /// </summary>
    ParityError = 0x02,

    /// <summary>
    /// Invalid source handle or address.
    /// </summary>
    InvalidSource = 0x03,

    /// <summary>
    /// Invalid destination handle or address.
    /// </summary>
    InvalidDestination = 0x04,

    /// <summary>
    /// Invalid length specified for copy operation.
    /// </summary>
    InvalidLength = 0x05,

    /// <summary>
    /// Invalid overlap between source and destination regions.
    /// </summary>
    InvalidOverlap = 0x06,

    /// <summary>
    /// A20 line error occurred.
    /// </summary>
    A20Error = 0x07
}