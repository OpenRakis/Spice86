namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
public sealed partial class PcSpeaker {
    #region Support Types

    /// <summary>
    /// State of PPI Port B (controls the PC Speaker)
    /// </summary>
    private class PpiPortB {
        public bool Timer2Gating { get; set; } = false;
        public bool SpeakerOutput { get; set; } = false;
    }
    
    #endregion
}