namespace Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Tracks the state of an in-progress or completed audio playback operation.</summary>
public sealed class CdAudioPlayback {
    /// <summary>Gets the logical block address where playback started.</summary>
    public int StartLba { get; }

    /// <summary>Gets the logical block address where playback ends (exclusive).</summary>
    public int EndLba { get; }

    /// <summary>Gets or sets the current read position as a logical block address.</summary>
    public int CurrentLba { get; set; }

    /// <summary>Gets or sets the current audio playback status.</summary>
    public CdAudioStatus Status { get; set; }

    /// <summary>Initialises a new <see cref="CdAudioPlayback"/> for the given LBA range.</summary>
    /// <param name="startLba">The logical block address where playback begins.</param>
    /// <param name="endLba">The logical block address where playback ends (exclusive).</param>
    public CdAudioPlayback(int startLba, int endLba) {
        StartLba = startLba;
        EndLba = endLba;
        CurrentLba = startLba;
        Status = CdAudioStatus.Stopped;
    }
}
