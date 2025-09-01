namespace Spice86.Core.Emulator.OperatingSystem.Structures;
/// <summary>
/// Represents a disk image mounted as a floppy disk drive.
/// </summary>
/// <remarks>Unimplemented.</remarks>
public class FloppyDiskImage : DosDriveBase {
    public FloppyDiskImage() {
        IsRemovable = true;
    }
}
