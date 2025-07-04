namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
public sealed partial class PcSpeaker {
    /// <summary>
    /// State of the PIT for PC Speaker
    /// </summary>
    private class PitState {
        // Timing state (in milliseconds)
        public float MaxMs { get; set; } = 0;
        public float HalfMs { get; set; } = 0;
        public float Mode1PendingMax { get; set; } = 0;
        
        // Mode state flags
        public bool Mode1WaitingForCounter { get; set; } = false;
        public bool Mode1WaitingForTrigger { get; set; } = true;
        public bool Mode3Counting { get; set; } = false;
        
        // Output state
        public PitMode Mode { get; set; } = PitMode.SquareWave;
        public float Amplitude { get; set; } = PositiveAmplitude;
    }
}