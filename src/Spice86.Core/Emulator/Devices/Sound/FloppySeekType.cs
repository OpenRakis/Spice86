namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Distinguishes whether the most recent floppy I/O was to the same file path
/// as the previous one (sequential) or a different path (random-access).
/// Mirrors DOSBox Staging's <c>DiskNoiseSeekType</c> enum.
/// </summary>
internal enum FloppySeekType {
    /// <summary>The current access targets the same file as the previous one.</summary>
    Sequential,

    /// <summary>The current access targets a different file from the previous one.</summary>
    RandomAccess,
}
