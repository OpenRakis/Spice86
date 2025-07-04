namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
public sealed partial class PcSpeaker {
    #region Support Types

    /// <summary>
    /// PIT operation modes
    /// </summary>
    private enum PitMode {
        InterruptOnTerminalCount = 0,
        OneShot = 1,
        RateGenerator = 2,
        SquareWave = 3,
        SoftwareStrobe = 4,
        HardwareStrobe = 5,
        RateGeneratorAlias = 6,
        SquareWaveAlias = 7,
        Inactive = 8
    }
    
    #endregion
}