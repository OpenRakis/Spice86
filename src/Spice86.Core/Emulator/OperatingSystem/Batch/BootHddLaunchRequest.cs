namespace Spice86.Core.Emulator.OperatingSystem.Batch;

/// <summary>
/// Represents a request to boot from a mounted hard-disk image, matching
/// DOSBox Staging's <c>BOOT [-l C..Z]</c> command. The MBR of the image is
/// inspected to select a partition (bootable indicator 0x80, else first
/// non-empty), the partition's first 512 bytes are loaded at physical
/// 0x7C00, and the CPU is reset to <c>0000:7C00</c> with <c>DL</c> set to
/// the BIOS hard-disk drive number (0x80 for the first HDD).
/// </summary>
internal sealed class BootHddLaunchRequest : LaunchRequest
{
    internal BootHddLaunchRequest(char driveLetter, CommandRedirection redirection)
        : base(redirection)
    {
        DriveLetter = driveLetter;
    }

    /// <summary>
    /// DOS drive letter (C..Z) of the hard-disk image to boot from.
    /// </summary>
    internal char DriveLetter { get; }

    internal override LaunchRequest WithRedirection(CommandRedirection redirection) =>
        new BootHddLaunchRequest(DriveLetter, redirection);
}
