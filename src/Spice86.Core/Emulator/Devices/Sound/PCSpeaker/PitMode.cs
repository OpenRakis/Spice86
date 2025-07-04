namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
/// <summary>
/// PIT operation modes (for the PC Speaker)
/// </summary>
internal enum PitMode : byte {
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
