namespace Spice86.Core.Emulator.Sound.Blaster;

internal static class Ports {
    public const int DspReset = 0x226;
    public const int DspReadData = 0x22A;
    public const int DspWrite = 0x22C;
    public const int DspReadBufferStatus = 0x22E;
    public const int DspDma16Acknowledge = 0x22F;

    public const int MixerAddress = 0x224;
    public const int MixerData = 0x225;
}
