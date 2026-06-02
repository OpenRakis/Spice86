namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Result object for floppy-geometry resolution.
/// </summary>
public readonly record struct FloppyGeometryResult(FloppyAccessStatus Status, FloppyGeometry Geometry) {
    public static FloppyGeometryResult DriveNotReady { get; } = new(FloppyAccessStatus.DriveNotReady, FloppyGeometry.Empty);

    public static FloppyGeometryResult Success(FloppyGeometry geometry) {
        return new FloppyGeometryResult(FloppyAccessStatus.Success, geometry);
    }
}
