namespace Spice86.Core.Emulator.OperatingSystem.Batch;

/// <summary>
/// Represents a request to boot from a mounted floppy image, matching
/// DOSBox Staging's <c>BOOT [-l A|B]</c> command. The boot sector (first 512
/// bytes) of the floppy image at <see cref="DriveLetter"/> is loaded at
/// physical address 0x7C00 and the CPU is reset to <c>0000:7C00</c> with
/// <c>DL</c> set to the floppy drive number (0 = A, 1 = B).
/// </summary>
internal sealed class BootFloppyLaunchRequest : LaunchRequest {
    internal BootFloppyLaunchRequest(char driveLetter, CommandRedirection redirection)
        : base(redirection) {
        DriveLetter = driveLetter;
    }

    /// <summary>
    /// DOS drive letter (A or B) of the floppy to boot from.
    /// </summary>
    internal char DriveLetter { get; }

    internal override LaunchRequest WithRedirection(CommandRedirection redirection) =>
        new BootFloppyLaunchRequest(DriveLetter, redirection);
}
