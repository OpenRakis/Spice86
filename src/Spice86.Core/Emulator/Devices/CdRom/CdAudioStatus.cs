namespace Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Represents the current audio playback status of a CD-ROM drive.</summary>
public enum CdAudioStatus {
    /// <summary>No audio is playing.</summary>
    Stopped = 0,

    /// <summary>Audio is currently playing.</summary>
    Playing = 1,

    /// <summary>Audio playback is paused.</summary>
    Paused = 2,

    /// <summary>An error occurred during playback.</summary>
    Error = 3,
}
