namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
/// <summary>
/// State of PPI Port B (controls the PC Speaker)
/// </summary>
public class PpiPortB {
    public bool Timer2Gating { get; set; } = false;
    public bool SpeakerOutput { get; set; } = false;
}
