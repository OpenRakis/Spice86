namespace Spice86.Emulator.Sound.Blaster;

internal static class Commands
{
    public const byte DirectModeOutput = 0x10;
    public const byte DirectModeInput = 0x20;

    public const byte SetTimeConstant = 0x40;

    public const byte SingleCycleDmaOutput8 = 0x14;

    public const byte PauseDmaMode = 0xD0;
    public const byte ContinueDmaMode = 0xD4;

    public const byte DspIdentification = 0xE0;
    public const byte GetVersionNumber = 0xE1;

    public const byte AutoInitDmaOutput8 = 0x1C;
    public const byte ExitAutoInit8 = 0xDA;

    public const byte HighSpeedAutoInitDmaOutput8 = 0x90;
    public const byte HighSpeedSingleCycleDmaOutput8 = 0x91;

    public const byte TurnOnSpeaker = 0xD1;
    public const byte TurnOffSpeaker = 0xD3;

    public const byte SetBlockTransferSize = 0x48;

    public const byte RaiseIrq8 = 0xF2;

    public const byte SetSampleRate = 0x41;
    public const byte SetInputSampleRate = 0x42;

    public const byte SingleCycleDmaOutput16 = 0xB0;
    public const byte SingleCycleDmaOutput16Fifo = 0xB2;
    public const byte AutoInitDmaOutput16 = 0xB4;
    public const byte AutoInitDmaOutput16Fifo = 0xB6;

    public const byte SingleCycleDmaOutput8_Alt = 0xC0;
    public const byte SingleCycleDmaOutput8Fifo_Alt = 0xC2;
    public const byte AutoInitDmaOutput8_Alt = 0xC4;
    public const byte AutoInitDmaOutput8Fifo_Alt = 0xC6;

    public const byte PauseDmaMode16 = 0xD5;
    public const byte ContinueDmaMode16 = 0xD6;
    public const byte ExitDmaMode16 = 0xD9;

    public const byte PauseForDuration = 0x80;

    public const byte SingleCycleDmaOutputADPCM4Ref = 0x75;
    public const byte SingleCycleDmaOutputADPCM4 = 0x7D;
    public const byte SingleCycleDmaOutputADPCM3Ref = 0x77;
    public const byte SingleCycleDmaOutputADPCM3 = 0x76;
    public const byte SingleCycleDmaOutputADPCM2Ref = 0x17;
    public const byte SingleCycleDmaOutputADPCM2 = 0x16;
}
