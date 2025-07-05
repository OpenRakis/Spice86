namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;

/// <summary>
/// State of PPI Port B (controls the PC Speaker)
/// </summary>
public class PpiPortB {
    /// <summary>
    /// Gets or sets a value indicating whether Timer2 gating is enabled.
    /// </summary>
    public bool Timer2Gating { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the speaker output is enabled.
    /// </summary>
    public bool SpeakerOutput { get; set; }
}
