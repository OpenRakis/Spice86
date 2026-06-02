namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Represents floppy-access operation status.
/// </summary>
public enum FloppyAccessStatus : byte {
    Success = 0,
    DriveNotReady = 1,
    OutOfRange = 2,
}
