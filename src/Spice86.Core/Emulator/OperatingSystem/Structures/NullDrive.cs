namespace Spice86.Core.Emulator.OperatingSystem.Structures;


internal class NullDrive : IVirtualDrive {
    private readonly char _driveLetter;
    private readonly bool _isRemovable;
    private readonly string _dosDriveRootPath;

    public NullDrive(char driveLetter, bool isRemovable, string dosDriveRootPath) {
        _driveLetter = driveLetter;
        _isRemovable = isRemovable;
        _dosDriveRootPath = dosDriveRootPath;
    }

    public string? Label { get; set; }
    public bool IsRemovable => _isRemovable;
    public bool IsReadOnlyMedium { get; }
    public char DriveLetter => _driveLetter;
    public string MountedHostDirectory { get; init; } = "";
    public string CurrentDosDirectory { get; set; } = "";
    public string DosDriveRootPath => _dosDriveRootPath;
}
