namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Geometry of an image-backed floppy drive.
/// </summary>
public readonly record struct FloppyGeometry(int TotalCylinders, int HeadsPerCylinder, int SectorsPerTrack, int BytesPerSector) {
    public static FloppyGeometry Empty { get; } = new(0, 0, 0, 0);
}
