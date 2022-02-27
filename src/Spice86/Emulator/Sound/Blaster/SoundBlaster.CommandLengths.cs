
namespace Spice86.Emulator.Sound.Blaster;

using System.Collections.Generic;

partial class SoundBlaster
{
    private static readonly SortedList<byte, byte> commandLengths = new SortedList<byte, byte>
    {
        [Commands.SetTimeConstant] = 1,
        [Commands.SingleCycleDmaOutput8] = 2,
        [Commands.DspIdentification] = 1,
        [Commands.SetBlockTransferSize] = 2,
        [Commands.SetSampleRate] = 2,
        [Commands.SetInputSampleRate] = 2,
        [Commands.SingleCycleDmaOutput16] = 3,
        [Commands.AutoInitDmaOutput16] = 3,
        [Commands.SingleCycleDmaOutput16Fifo] = 3,
        [Commands.AutoInitDmaOutput16Fifo] = 3,
        [Commands.SingleCycleDmaOutput8_Alt] = 3,
        [Commands.AutoInitDmaOutput8_Alt] = 3,
        [Commands.SingleCycleDmaOutput8Fifo_Alt] = 3,
        [Commands.AutoInitDmaOutput8Fifo_Alt] = 3,
        [Commands.PauseForDuration] = 2,
        [Commands.SingleCycleDmaOutputADPCM4Ref] = 2,
        [Commands.SingleCycleDmaOutputADPCM2Ref] = 2,
        [Commands.SingleCycleDmaOutputADPCM3Ref] = 2
    };
}
