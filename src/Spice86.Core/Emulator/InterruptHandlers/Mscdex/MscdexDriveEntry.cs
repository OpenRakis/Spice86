namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

using Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Associates a CD-ROM drive implementation with its DOS drive letter and zero-based index.</summary>
public sealed class MscdexDriveEntry {
    /// <summary>Gets the DOS drive letter (e.g. 'D').</summary>
    public char DriveLetter { get; }

    /// <summary>Gets the zero-based drive index (A=0, B=1, C=2, D=3, …).</summary>
    public byte DriveIndex { get; }

    /// <summary>Gets the underlying CD-ROM drive implementation.</summary>
    public ICdRomDrive Drive { get; }

    /// <summary>Initialises a new <see cref="MscdexDriveEntry"/>.</summary>
    /// <param name="driveLetter">The DOS drive letter (e.g. 'D').</param>
    /// <param name="driveIndex">The zero-based drive index (A=0, B=1, C=2, D=3, …).</param>
    /// <param name="drive">The underlying CD-ROM drive implementation.</param>
    public MscdexDriveEntry(char driveLetter, byte driveIndex, ICdRomDrive drive) {
        DriveLetter = driveLetter;
        DriveIndex = driveIndex;
        Drive = drive;
    }
}
