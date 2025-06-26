namespace Spice86.Core.Emulator.InterruptHandlers.Bios.Enums;
public enum ExtendedMemoryCopyStatus : byte {
    SourceCopiedIntoDest = 0x0,
    ParityError = 0x1,
    InterruptError = 0x2,
    Address20HGattingFailed = 0x3,
    /// <summary>
    /// Specific to the original IBM PC, and the PC XT architectures. Always returns this.
    /// </summary>
    InvalidCommand = 0x80,
    /// <summary>
    /// Specific to XT and PS30 architectures. Always returns this.
    /// </summary>
    UnsupportedFunction = 0x86
}
