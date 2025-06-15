namespace Spice86.Core.Emulator.OperatingSystem.Structures;

/// <inheritdoc cref="IVirtualDrive" />
internal class NullDrive : IVirtualDrive {
    private readonly char _driveLetter;
    private readonly bool _isRemovable;

    public NullDrive(char driveLetter, bool isRemovable) {
        _driveLetter = driveLetter;
        _isRemovable = isRemovable;
    }

    public string? Label { get; set; }
    public bool IsRemovable => _isRemovable;
    public bool IsReadOnlyMedium { get; }
    public char DriveLetter => _driveLetter;
    public string MountedHostDirectory { get; init; } = "";
    public string CurrentDosDirectory { get; set; } = "";
    public string DosVolume => $"{DriveLetter}{DosPathResolver.VolumeSeparatorChar}";
}
