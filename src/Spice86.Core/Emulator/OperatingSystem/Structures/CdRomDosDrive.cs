namespace Spice86.Core.Emulator.OperatingSystem.Structures;

/// <summary>
/// Represents a DOS-visible CD-ROM drive letter.
/// </summary>
public sealed class CdRomDosDrive : VirtualDrive {
    /// <summary>
    /// Initializes a new <see cref="CdRomDosDrive"/>.
    /// </summary>
    public CdRomDosDrive() {
        IsRemovable = true;
    }
}