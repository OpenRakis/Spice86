namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Result object for floppy image read/write operations.
/// </summary>
public readonly record struct FloppyTransferResult(FloppyAccessStatus Status, int BytesTransferred) {
    public static FloppyTransferResult DriveNotReady { get; } = new(FloppyAccessStatus.DriveNotReady, 0);
    public static FloppyTransferResult OutOfRange { get; } = new(FloppyAccessStatus.OutOfRange, 0);

    public static FloppyTransferResult Success(int bytesTransferred) {
        return new FloppyTransferResult(FloppyAccessStatus.Success, bytesTransferred);
    }
}
