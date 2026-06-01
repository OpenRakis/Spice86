namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Controls the floppy drive noise emulation level.
/// Mirrors DOSBox Staging's <c>DiskNoiseMode</c> enum.
/// </summary>
public enum FloppyDiskNoiseMode {
    /// <summary>No floppy sounds are played.</summary>
    Off,

    /// <summary>Only head-seek sounds are played; the motor/spin sound is suppressed.</summary>
    SeekOnly,

    /// <summary>Both seek sounds and motor/spin sounds are played.</summary>
    On,
}
