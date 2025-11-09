// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed partial class Opl3Chip {
    /* Original C: static void OPL3_PhaseGenerate(opl3_slot *slot) */
    private static void PhaseGenerate(Opl3Operator slot) {
        Opl3Chip chip = slot.Chip ?? throw new InvalidOperationException("Slot chip not assigned.");
        Opl3Channel channel = slot.Channel ?? throw new InvalidOperationException("Slot channel not assigned.");

        int fNum = channel.FNumber;
        if (slot.RegVibrato != 0) {
            int range = (fNum >> 7) & 0x07;
            byte vibPos = chip.VibratoPosition;

            if ((vibPos & 0x03) == 0) {
                range = 0;
            } else if ((vibPos & 0x01) != 0) {
                range >>= 1;
            }

            range >>= chip.VibratoShift;

            if ((vibPos & 0x04) != 0) {
                range = -range;
            }

            fNum = unchecked(fNum + range);
        }

        uint baseFreq = unchecked((uint)((fNum << channel.Block) >> 1));
        ushort phase = (ushort)(slot.RegPhaseGeneratorAccumulator >> 9);

        if (slot.RegPhaseResetRequest != 0) {
            slot.RegPhaseGeneratorAccumulator = 0;
        }

        byte freqMultiplier = Opl3Tables.ReadFrequencyMultiplier(slot.RegFrequencyMultiplier);
        slot.RegPhaseGeneratorAccumulator =
            unchecked(slot.RegPhaseGeneratorAccumulator + ((baseFreq * freqMultiplier) >> 1));

        uint noise = chip.Noise;
        slot.PhaseGeneratorOutput = phase;

        switch (slot.SlotIndex) {
            /* hh */
            case 13:
                chip.RhythmHihatBit2 = (byte)((phase >> 2) & 1);
                chip.RhythmHihatBit3 = (byte)((phase >> 3) & 1);
                chip.RhythmHihatBit7 = (byte)((phase >> 7) & 1);
                chip.RhythmHihatBit8 = (byte)((phase >> 8) & 1);
                break;
            /* tc */
            case 17 when (chip.Rhythm & 0x20) != 0:
                chip.RhythmTomBit3 = (byte)((phase >> 3) & 1);
                chip.RhythmTomBit5 = (byte)((phase >> 5) & 1);
                break;
        }

        if ((chip.Rhythm & 0x20) != 0) {
            byte rmXor = (byte)(((chip.RhythmHihatBit2 ^ chip.RhythmHihatBit7)
                                 | (chip.RhythmHihatBit3 ^ chip.RhythmTomBit5)
                                 | (chip.RhythmTomBit3 ^ chip.RhythmTomBit5)) & 0x01);
            switch (slot.SlotIndex) {
                case 13: /* hh */
                    slot.PhaseGeneratorOutput = (ushort)(rmXor << 9);
                    if (((rmXor ^ noise) & 0x01) != 0) {
                        slot.PhaseGeneratorOutput = unchecked((ushort)(slot.PhaseGeneratorOutput | 0xd0));
                    } else {
                        slot.PhaseGeneratorOutput = unchecked((ushort)(slot.PhaseGeneratorOutput | 0x34));
                    }

                    break;
                case 16: /* sd */ {
                    byte noiseBit = (byte)(noise & 0x01);
                    slot.PhaseGeneratorOutput = (ushort)(((chip.RhythmHihatBit8 & 0x01) << 9)
                                                         | (((chip.RhythmHihatBit8 ^ noiseBit) & 0x01) << 8));
                    break;
                }
                case 17: /* tc */
                    slot.PhaseGeneratorOutput = unchecked((ushort)((rmXor << 9) | 0x80));
                    break;
            }
        }

        uint nBit = ((noise >> 14) ^ noise) & 0x01;
        chip.Noise = (noise >> 1) | (nBit << 22);
    }

    /* Original C: static void OPL3_SlotWrite20(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite20(Opl3Operator slot, byte data) {
        slot.TremoloEnabled = ((data >> 7) & 0x01) != 0;
        slot.RegVibrato = (byte)((data >> 6) & 0x01);
        slot.RegOperatorType = (byte)((data >> 5) & 0x01);
        slot.RegKeyScaleRate = (byte)((data >> 4) & 0x01);
        slot.RegFrequencyMultiplier = (byte)(data & 0x0f);
    }

    /* Original C: static void OPL3_SlotWrite40(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite40(Opl3Operator slot, byte data) {
        slot.RegKeyScaleLevel = (byte)((data >> 6) & 0x03);
        slot.RegTotalLevel = (byte)(data & 0x3f);
        Opl3Envelope.EnvelopeUpdateKsl(slot);
    }

    /* Original C: static void OPL3_SlotWrite60(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite60(Opl3Operator slot, byte data) {
        slot.RegAttackRate = (byte)((data >> 4) & 0x0f);
        slot.RegDecayRate = (byte)(data & 0x0f);
    }

    /* Original C: static void OPL3_SlotWrite80(opl3_slot *slot, uint8_t data) */
    private static void SlotWrite80(Opl3Operator slot, byte data) {
        slot.RegSustainLevel = (byte)((data >> 4) & 0x0f);
        if (slot.RegSustainLevel == 0x0f) {
            slot.RegSustainLevel = 0x1f;
        }

        slot.RegReleaseRate = (byte)(data & 0x0f);
    }

    /* Original C: static void OPL3_SlotWriteE0(opl3_slot *slot, uint8_t data) */
    private static void SlotWriteE0(Opl3Operator slot, byte data) {
        slot.RegWaveformSelect = (byte)(data & 0x07);
        if (slot.Chip?.NewM == 0) {
            slot.RegWaveformSelect &= 0x03;
        }
    }

    /* Original C: static void OPL3_SlotGenerate(opl3_slot *slot) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SlotGenerate(Opl3Operator slot) {
        slot.Out = Opl3Envelope.GenerateWaveform(slot);
    }

    /* Original C: static void OPL3_SlotCalcFB(opl3_slot *slot) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SlotCalcFeedback(Opl3Operator slot) {
        Opl3Channel channel = slot.Channel ?? throw new InvalidOperationException("Slot channel not assigned.");
        if (channel.Feedback != 0) {
            slot.FeedbackModifiedSignal = (short)((slot.PreviousOutputSample + slot.Out) >> (0x09 - channel.Feedback));
        } else {
            slot.FeedbackModifiedSignal = 0;
        }

        slot.PreviousOutputSample = slot.Out;
    }

    /* Original C: static void OPL3_ChannelSetupAlg(opl3_channel *channel) */
    private static void ChannelSetupAlgorithm(Opl3Channel channel) {
        if (channel.ChannelType == ChannelType.Drum) {
            if (channel.ChannelNumber is 7 or 8) {
                channel.Slotz[0].ModulationSource = ShortSignalSource.Zero;
                channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                return;
            }

            switch (channel.Algorithm & 0x01) {
                case 0x00:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    break;
                case 0x01:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    break;
            }

            return;
        }

        if ((channel.Algorithm & 0x08) != 0) {
            return;
        }

        if ((channel.Algorithm & 0x04) != 0) {
            Opl3Channel pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
            pair.Out[0] = ShortSignalSource.Zero;
            pair.Out[1] = ShortSignalSource.Zero;
            pair.Out[2] = ShortSignalSource.Zero;
            pair.Out[3] = ShortSignalSource.Zero;

            switch (channel.Algorithm & 0x03) {
                case 0x00:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = pair.Slotz[0].OutputSignal;
                    channel.Slotz[0].ModulationSource = pair.Slotz[1].OutputSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = channel.Slotz[1].OutputSignal;
                    channel.Out[1] = ShortSignalSource.Zero;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    break;
                case 0x01:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = pair.Slotz[0].OutputSignal;
                    channel.Slotz[0].ModulationSource = ShortSignalSource.Zero;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = pair.Slotz[1].OutputSignal;
                    channel.Out[1] = channel.Slotz[1].OutputSignal;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    break;
                case 0x02:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Slotz[0].ModulationSource = pair.Slotz[1].OutputSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = pair.Slotz[0].OutputSignal;
                    channel.Out[1] = channel.Slotz[1].OutputSignal;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    break;
                case 0x03:
                    pair.Slotz[0].ModulationSource = pair.Slotz[0].FeedbackSignal;
                    pair.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Slotz[0].ModulationSource = pair.Slotz[1].OutputSignal;
                    channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Out[0] = pair.Slotz[0].OutputSignal;
                    channel.Out[1] = channel.Slotz[0].OutputSignal;
                    channel.Out[2] = channel.Slotz[1].OutputSignal;
                    channel.Out[3] = ShortSignalSource.Zero;
                    break;
            }
        } else {
            switch (channel.Algorithm & 0x01) {
                case 0x00:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = channel.Slotz[0].OutputSignal;
                    channel.Out[0] = channel.Slotz[1].OutputSignal;
                    channel.Out[1] = ShortSignalSource.Zero;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    break;
                case 0x01:
                    channel.Slotz[0].ModulationSource = channel.Slotz[0].FeedbackSignal;
                    channel.Slotz[1].ModulationSource = ShortSignalSource.Zero;
                    channel.Out[0] = channel.Slotz[0].OutputSignal;
                    channel.Out[1] = channel.Slotz[1].OutputSignal;
                    channel.Out[2] = ShortSignalSource.Zero;
                    channel.Out[3] = ShortSignalSource.Zero;
                    break;
            }
        }
    }

    /* Original C: static void OPL3_ChannelUpdateRhythm(opl3_chip *chip, uint8_t data) */
    private static void ChannelUpdateRhythm(Opl3Chip chip, byte data) {
        chip.Rhythm = (byte)(data & 0x3f);

        if ((chip.Rhythm & 0x20) != 0) {
            Opl3Channel channel6 = chip.Channels[6];
            Opl3Channel channel7 = chip.Channels[7];
            Opl3Channel channel8 = chip.Channels[8];

            channel6.Out[0] = channel6.Slotz[1].OutputSignal;
            channel6.Out[1] = channel6.Slotz[1].OutputSignal;
            channel6.Out[2] = ShortSignalSource.Zero;
            channel6.Out[3] = ShortSignalSource.Zero;

            channel7.Out[0] = channel7.Slotz[0].OutputSignal;
            channel7.Out[1] = channel7.Slotz[0].OutputSignal;
            channel7.Out[2] = channel7.Slotz[1].OutputSignal;
            channel7.Out[3] = channel7.Slotz[1].OutputSignal;

            channel8.Out[0] = channel8.Slotz[0].OutputSignal;
            channel8.Out[1] = channel8.Slotz[0].OutputSignal;
            channel8.Out[2] = channel8.Slotz[1].OutputSignal;
            channel8.Out[3] = channel8.Slotz[1].OutputSignal;

            for (int ch = 6; ch < 9; ch++) {
                chip.Channels[ch].ChannelType = ChannelType.Drum;
            }

            ChannelSetupAlgorithm(channel6);
            ChannelSetupAlgorithm(channel7);
            ChannelSetupAlgorithm(channel8);

            if ((chip.Rhythm & 0x01) != 0) /* hh */ {
                Opl3Envelope.EnvelopeKeyOn(channel7.Slotz[0], EnvelopeKeyType.Drum);
            } else {
                Opl3Envelope.EnvelopeKeyOff(channel7.Slotz[0], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x02) != 0) /* tc */ {
                Opl3Envelope.EnvelopeKeyOn(channel8.Slotz[1], EnvelopeKeyType.Drum);
            } else {
                Opl3Envelope.EnvelopeKeyOff(channel8.Slotz[1], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x04) != 0) /* tom */ {
                Opl3Envelope.EnvelopeKeyOn(channel8.Slotz[0], EnvelopeKeyType.Drum);
            } else {
                Opl3Envelope.EnvelopeKeyOff(channel8.Slotz[0], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x08) != 0) /* sd */ {
                Opl3Envelope.EnvelopeKeyOn(channel7.Slotz[1], EnvelopeKeyType.Drum);
            } else {
                Opl3Envelope.EnvelopeKeyOff(channel7.Slotz[1], EnvelopeKeyType.Drum);
            }

            if ((chip.Rhythm & 0x10) != 0) /* bd */ {
                Opl3Envelope.EnvelopeKeyOn(channel6.Slotz[0], EnvelopeKeyType.Drum);
                Opl3Envelope.EnvelopeKeyOn(channel6.Slotz[1], EnvelopeKeyType.Drum);
            } else {
                Opl3Envelope.EnvelopeKeyOff(channel6.Slotz[0], EnvelopeKeyType.Drum);
                Opl3Envelope.EnvelopeKeyOff(channel6.Slotz[1], EnvelopeKeyType.Drum);
            }
        } else {
            for (int ch = 6; ch < 9; ch++) {
                Opl3Channel channel = chip.Channels[ch];
                channel.ChannelType = ChannelType.TwoOp;
                channel.Out[0] = ShortSignalSource.Zero;
                channel.Out[1] = ShortSignalSource.Zero;
                channel.Out[2] = ShortSignalSource.Zero;
                channel.Out[3] = ShortSignalSource.Zero;
                ChannelSetupAlgorithm(channel);
            }
        }
    }

    /* Original C: static void OPL3_ChannelWriteA0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteA0(Opl3Channel channel, byte data) {
        channel.FNumber = (ushort)((channel.FNumber & 0x300) | data);
        if (channel.Chip?.NewM == 0 || channel.ChannelType != ChannelType.FourOp) {
            return;
        }

        Opl3Channel pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
        pair.FNumber = channel.FNumber;
    }

    /* Original C: static void OPL3_ChannelWriteB0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteB0(Opl3Channel channel, byte data) {
        channel.FNumber = (ushort)((channel.FNumber & 0xff) | ((data & 0x03) << 8));
        channel.Block = (byte)((data >> 2) & 0x07);
        channel.KeyScaleValue = (byte)((channel.Block << 1)
                                       | ((channel.FNumber >> (0x09 - (channel.Chip?.Nts ?? 0))) & 0x01));
        Opl3Envelope.EnvelopeUpdateKsl(channel.Slotz[0]);
        Opl3Envelope.EnvelopeUpdateKsl(channel.Slotz[1]);

        if (channel.Chip?.NewM == 0 || channel.ChannelType != ChannelType.FourOp) {
            return;
        }

        Opl3Channel pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
        pair.FNumber = channel.FNumber;
        pair.Block = channel.Block;
        pair.KeyScaleValue = channel.KeyScaleValue;
        Opl3Envelope.EnvelopeUpdateKsl(pair.Slotz[0]);
        Opl3Envelope.EnvelopeUpdateKsl(pair.Slotz[1]);
    }

    /* Original C: static void OPL3_ChannelUpdateAlg(opl3_channel *channel) */
    private static void ChannelUpdateAlgorithm(Opl3Channel channel) {
        channel.Algorithm = channel.Connection;
        Opl3Chip chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.NewM != 0) {
            switch (channel.ChannelType) {
                case ChannelType.FourOp: {
                    Opl3Channel pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
                    pair.Algorithm = (byte)(0x04 | (channel.Connection << 1) | pair.Connection);
                    channel.Algorithm = 0x08;
                    ChannelSetupAlgorithm(pair);
                    break;
                }
                case ChannelType.FourOpPair: {
                    Opl3Channel primary = channel.Pair ?? throw new InvalidOperationException("Missing 4-op primary.");
                    channel.Algorithm = (byte)(0x04 | (primary.Connection << 1) | channel.Connection);
                    primary.Algorithm = 0x08;
                    ChannelSetupAlgorithm(channel);
                    break;
                }
                default:
                    ChannelSetupAlgorithm(channel);
                    break;
            }
        } else {
            ChannelSetupAlgorithm(channel);
        }
    }

    /* Original C: static void OPL3_ChannelWriteC0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteC0(Opl3Channel channel, byte data) {
        channel.Feedback = (byte)((data & 0x0e) >> 1);
        channel.Connection = (byte)(data & 0x01);
        ChannelUpdateAlgorithm(channel);

        if (channel.Chip?.NewM != 0) {
            channel.Cha = (ushort)(((data >> 4) & 0x01) != 0 ? 0xffff : 0);
            channel.Chb = (ushort)(((data >> 5) & 0x01) != 0 ? 0xffff : 0);
            channel.Chc = (ushort)(((data >> 6) & 0x01) != 0 ? 0xffff : 0);
            channel.Chd = (ushort)(((data >> 7) & 0x01) != 0 ? 0xffff : 0);
        } else {
            channel.Cha = 0xffff;
            channel.Chb = 0xffff;
            channel.Chc = 0;
            channel.Chd = 0;
        }
#if OPL_ENABLE_STEREOEXT
        if (channel.Chip is { StereoExtension: 0 }) {
            channel.LeftPan = channel.Cha != 0 ? 0x10000 : 0;
            channel.RightPan = channel.Chb != 0 ? 0x10000 : 0;
        }
#endif
    }

#if OPL_ENABLE_STEREOEXT
    /* Original C: static void OPL3_ChannelWriteD0(opl3_channel *channel, uint8_t data) */
    private static void ChannelWriteD0(Opl3Channel channel, byte data) {
        Opl3Chip chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.StereoExtension == 0) {
            return;
        }

        ReadOnlySpan<int> panPot = Opl3Tables.StereoPanPotLut;
        int leftIndex = data ^ 0xff;
        channel.LeftPan = panPot[leftIndex];
        channel.RightPan = panPot[data];
    }
#endif

    /* Original C: static void OPL3_ChannelKeyOn(opl3_channel *channel) */
    private static void ChannelKeyOn(Opl3Channel channel) {
        Opl3Chip chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.NewM != 0) {
            switch (channel.ChannelType) {
                case ChannelType.FourOp: {
                    Opl3Channel pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
                    Opl3Envelope.EnvelopeKeyOn(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOn(channel.Slotz[1], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOn(pair.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOn(pair.Slotz[1], EnvelopeKeyType.Normal);
                    break;
                }
                case ChannelType.TwoOp or ChannelType.Drum:
                    Opl3Envelope.EnvelopeKeyOn(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOn(channel.Slotz[1], EnvelopeKeyType.Normal);
                    break;
            }
        } else {
            Opl3Envelope.EnvelopeKeyOn(channel.Slotz[0], EnvelopeKeyType.Normal);
            Opl3Envelope.EnvelopeKeyOn(channel.Slotz[1], EnvelopeKeyType.Normal);
        }
    }

    /* Original C: static void OPL3_ChannelKeyOff(opl3_channel *channel) */
    private static void ChannelKeyOff(Opl3Channel channel) {
        Opl3Chip chip = channel.Chip ?? throw new InvalidOperationException("Channel chip not assigned.");

        if (chip.NewM != 0) {
            switch (channel.ChannelType) {
                case ChannelType.FourOp: {
                    Opl3Channel pair = channel.Pair ?? throw new InvalidOperationException("Missing 4-op pair.");
                    Opl3Envelope.EnvelopeKeyOff(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOff(channel.Slotz[1], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOff(pair.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOff(pair.Slotz[1], EnvelopeKeyType.Normal);
                    break;
                }
                case ChannelType.TwoOp:
                case ChannelType.Drum:
                    Opl3Envelope.EnvelopeKeyOff(channel.Slotz[0], EnvelopeKeyType.Normal);
                    Opl3Envelope.EnvelopeKeyOff(channel.Slotz[1], EnvelopeKeyType.Normal);
                    break;
            }
        } else {
            Opl3Envelope.EnvelopeKeyOff(channel.Slotz[0], EnvelopeKeyType.Normal);
            Opl3Envelope.EnvelopeKeyOff(channel.Slotz[1], EnvelopeKeyType.Normal);
        }
    }

    /* Original C: static void OPL3_ChannelSet4Op(opl3_chip *chip, uint8_t data) */
    private static void ChannelSet4Op(Opl3Chip chip, byte data) {
        for (byte bit = 0; bit < 6; bit++) {
            byte chNum = bit;
            if (bit >= 3) {
                chNum = (byte)(bit + 6);
            }

            if (((data >> bit) & 0x01) != 0) {
                chip.Channels[chNum].ChannelType = ChannelType.FourOp;
                chip.Channels[chNum + 3].ChannelType = ChannelType.FourOpPair;
                ChannelUpdateAlgorithm(chip.Channels[chNum]);
            } else {
                chip.Channels[chNum].ChannelType = ChannelType.TwoOp;
                chip.Channels[chNum + 3].ChannelType = ChannelType.TwoOp;
                ChannelUpdateAlgorithm(chip.Channels[chNum]);
                ChannelUpdateAlgorithm(chip.Channels[chNum + 3]);
            }
        }
    }

    /* Original C: static int16_t OPL3_ClipSample(int32_t sample) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short ClipSample(int sample) {
        return sample switch {
            > short.MaxValue => short.MaxValue,
            < short.MinValue => short.MinValue,
            _ => (short)sample
        };
    }

    /* Original C: static void OPL3_ProcessSlot(opl3_slot *slot) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessSlot(Opl3Operator slot) {
        Opl3Envelope.EnvelopeCalc(slot);
        PhaseGenerate(slot);
        SlotGenerate(slot);
        SlotCalcFeedback(slot);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumChannelOutputs(Opl3Channel channel) {
        ShortSignalSource[] outputs = channel.Out;
        ref ShortSignalSource first = ref MemoryMarshal.GetArrayDataReference(outputs);
        return first.Read()
               + Unsafe.Add(ref first, 1).Read()
               + Unsafe.Add(ref first, 2).Read()
               + Unsafe.Add(ref first, 3).Read();
    }

    /* Original C: void OPL3_Generate4Ch(opl3_chip *chip, int16_t *buf4) */
    private void Generate4ChCore(Span<short> buffer) {
        if (buffer.Length < 4) {
            throw new ArgumentException("Buffer must contain at least four samples.", nameof(buffer));
        }

        buffer[1] = ClipSample(MixBuffer[1]);
        buffer[3] = ClipSample(MixBuffer[3]);

#if !OPL_ENABLE_STEREOEXT
        for (int ii = 0; ii < 15; ii++) {
            ProcessSlot(Slots[ii]);
        }
#else
        Opl3Operator[] localSlots = Slots;
        for (int ii = 0; ii < localSlots.Length; ii++) {
            ProcessSlot(localSlots[ii]);
        }
#endif

        int mix0 = 0;
        int mix1 = 0;
        Opl3Channel[] channels = Channels;
        for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++) {
            Opl3Channel channel = channels[channelIndex];
            int accm = SumChannelOutputs(channel);
#if OPL_ENABLE_STEREOEXT
            int panLeft = (int)(((long)accm * channel.LeftPan) >> 16);
            mix0 = unchecked(mix0 + (short)panLeft);
#else
            mix0 = unchecked(mix0 + (short)(accm & channel.Cha));
#endif

            int maskChc = channel.Chc;
            mix1 = unchecked(mix1 + (short)(accm & maskChc));
        }

        MixBuffer[0] = mix0;
        MixBuffer[2] = mix1;

#if !OPL_ENABLE_STEREOEXT
        for (int ii = 15; ii < 18; ii++) {
            ProcessSlot(Slots[ii]);
        }
#endif

        buffer[0] = ClipSample(MixBuffer[0]);
        buffer[2] = ClipSample(MixBuffer[2]);

#if !OPL_ENABLE_STEREOEXT
        for (int ii = 18; ii < 33; ii++) {
            ProcessSlot(Slots[ii]);
        }
#endif

        mix0 = 0;
        mix1 = 0;
        for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++) {
            Opl3Channel channel = channels[channelIndex];
            int accm = SumChannelOutputs(channel);

#if OPL_ENABLE_STEREOEXT
            int panRight = (int)(((long)accm * channel.RightPan) >> 16);
            mix0 = unchecked(mix0 + (short)panRight);
#else
            mix0 = unchecked(mix0 + (short)(accm & channel.Chb));
#endif

            int maskChd = channel.Chd;
            mix1 = unchecked(mix1 + (short)(accm & maskChd));
        }

        MixBuffer[1] = mix0;
        MixBuffer[3] = mix1;
#if !OPL_ENABLE_STEREOEXT
        for (int ii = 33; ii < Slots.Length; ii++) {
            ProcessSlot(Slots[ii]);
        }
#endif

        Opl3Lfo.Advance(this);

        Timer++;

        if (EgState != 0) {
            byte shift = 0;
            while (shift < 13 && ((EgTimer >> shift) & 1) == 0) {
                shift++;
            }

            if (shift > 12) {
                EgAdd = 0;
            } else {
                EgAdd = (byte)(shift + 1);
            }

            EgTimerLow = (byte)(EgTimer & 0x03u);
        }

        if (EgTimerRem != 0 || EgState != 0) {
            if (EgTimer == 0x0FFFFFFFFFUL) {
                EgTimer = 0;
                EgTimerRem = 1;
            } else {
                EgTimer++;
                EgTimerRem = 0;
            }
        }

        EgState ^= 1;

        while (true) {
            Opl3WriteBufferEntry entry = WriteBuffer[(int)WriteBufferCurrent];
            if (entry.Time > WriteBufferSampleCounter) {
                break;
            }

            if ((entry.Register & 0x200) == 0) {
                break;
            }

            ushort reg = (ushort)(entry.Register & 0x1ff);
            entry.Register = reg;
            WriteRegisterInternal(reg, entry.Data);
            WriteBufferCurrent = (WriteBufferCurrent + 1) % WriteBufferSize;
        }

        WriteBufferSampleCounter++;
    }

    /* Original C: void OPL3_Generate(opl3_chip *chip, int16_t *buf) */
    private void GenerateCore(Span<short> buffer) {
        if (buffer.Length < 2) {
            throw new ArgumentException("Buffer must contain at least two samples.", nameof(buffer));
        }

        Span<short> temp = stackalloc short[4];
        Generate4ChCore(temp);
        buffer[0] = temp[0];
        buffer[1] = temp[1];
    }

    /* Original C: void OPL3_Generate4ChResampled(opl3_chip *chip, int16_t *buf4) */
    private void Generate4ChResampledCore(Span<short> buffer) {
        if (buffer.Length < 4) {
            throw new ArgumentException("Buffer must contain at least four samples.", nameof(buffer));
        }

        ref short destination = ref MemoryMarshal.GetReference(buffer);
        Generate4ChResampledCore(
            ref destination,
            ref Unsafe.Add(ref destination, 1),
            ref Unsafe.Add(ref destination, 2),
            ref Unsafe.Add(ref destination, 3));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Generate4ChResampledCore(
        // ReSharper disable RedundantAssignment
        ref short channel0,
        ref short channel1,
        ref short channel2,
        ref short channel3
        // ReSharper restore RedundantAssignment
    ) {
        while (RateRatio != 0 && SampleCounter >= RateRatio) {
            OldSamples[0] = Samples[0];
            OldSamples[1] = Samples[1];
            OldSamples[2] = Samples[2];
            OldSamples[3] = Samples[3];

            Generate4ChCore(Samples.AsSpan());
            SampleCounter -= RateRatio;
        }

        if (RateRatio != 0) {
            channel0 = (short)(((OldSamples[0] * (RateRatio - SampleCounter)) + (Samples[0] * SampleCounter)) /
                               RateRatio);
            channel1 = (short)(((OldSamples[1] * (RateRatio - SampleCounter)) + (Samples[1] * SampleCounter)) /
                               RateRatio);
            channel2 = (short)(((OldSamples[2] * (RateRatio - SampleCounter)) + (Samples[2] * SampleCounter)) /
                               RateRatio);
            channel3 = (short)(((OldSamples[3] * (RateRatio - SampleCounter)) + (Samples[3] * SampleCounter)) /
                               RateRatio);
        } else {
            channel0 = Samples[0];
            channel1 = Samples[1];
            channel2 = Samples[2];
            channel3 = Samples[3];
        }

        SampleCounter += 1 << ResampleFractionBits;
    }

    /* Original C: void OPL3_GenerateResampled(opl3_chip *chip, int16_t *buf) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GenerateResampledCore(Span<short> buffer) {
        if (buffer.Length < 2) {
            throw new ArgumentException("Buffer must contain at least two samples.", nameof(buffer));
        }

        ref short destination = ref MemoryMarshal.GetReference(buffer);
        short discardRearLeft = 0;
        short discardRearRight = 0;
        Generate4ChResampledCore(
            ref destination,
            ref Unsafe.Add(ref destination, 1),
            ref discardRearLeft,
            ref discardRearRight);
    }

    /* Original C: void OPL3_Reset(opl3_chip *chip, uint32_t samplerate) */
    private void ResetInternal(uint sampleRate) {
        Timer = 0;
        EgTimer = 0;
        EgTimerRem = 0;
        EgState = 0;
        EgAdd = 0;
        EgTimerLow = 0;
        NewM = 0;
        Nts = 0;
        Rhythm = 0;
        Opl3Lfo.Reset(this);
        Noise = 1;
        ZeroMod = 0;
        Array.Clear(MixBuffer, 0, MixBuffer.Length);
        RhythmHihatBit2 = 0;
        RhythmHihatBit3 = 0;
        RhythmHihatBit7 = 0;
        RhythmHihatBit8 = 0;
        RhythmTomBit3 = 0;
        RhythmTomBit5 = 0;
#if OPL_ENABLE_STEREOEXT
        StereoExtension = 0;
#endif
        RateRatio = sampleRate == 0 ? 0 : (int)((sampleRate << ResampleFractionBits) / 49716);
        if (RateRatio == 0) {
            RateRatio = 1;
        }

        SampleCounter = 0;
        Array.Clear(OldSamples, 0, OldSamples.Length);
        Array.Clear(Samples, 0, Samples.Length);
        WriteBufferSampleCounter = 0;
        WriteBufferCurrent = 0;
        WriteBufferLast = 0;
        WriteBufferLastTime = 0;

        Opl3WriteBufferEntry[] writeBuffer = WriteBuffer;
        for (int i = 0; i < writeBuffer.Length; i++) {
            Opl3WriteBufferEntry entry = writeBuffer[i];
            entry.Register = 0;
            entry.Data = 0;
            entry.Time = 0;
        }

        for (int slotIndex = 0; slotIndex < Slots.Length; slotIndex++) {
            Opl3Operator slot = Slots[slotIndex];
            slot.Channel = null;
            slot.Chip = this;
            slot.ModulationSource = ShortSignalSource.Zero;
            slot.PreviousOutputSample = 0;
            slot.Out = 0;
            slot.FeedbackModifiedSignal = 0;
            slot.EnvelopeGeneratorOutput = 0x1ff;
            slot.EnvelopeGeneratorLevel = 0x1ff;
            slot.EnvelopeGeneratorIncrement = 0;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;
            slot.EffectiveEnvelopeRateIndex = 0;
            slot.EffectiveKeyScaleLevel = 0;
            slot.TremoloEnabled = false;
            slot.RegVibrato = 0;
            slot.RegOperatorType = 0;
            slot.RegKeyScaleRate = 0;
            slot.RegFrequencyMultiplier = 0;
            slot.RegKeyScaleLevel = 0;
            slot.RegTotalLevel = 0;
            slot.RegAttackRate = 0;
            slot.RegDecayRate = 0;
            slot.RegSustainLevel = 0;
            slot.RegReleaseRate = 0;
            slot.RegWaveformSelect = 0;
            slot.RegKeyState = 0;
            slot.RegPhaseResetRequest = 0;
            slot.RegPhaseGeneratorAccumulator = 0;
            slot.PhaseGeneratorOutput = 0;
            slot.SlotIndex = (byte)slotIndex;
        }

        for (int channelIndex = 0; channelIndex < Channels.Length; channelIndex++) {
            Opl3Channel channel = Channels[channelIndex];
            byte localSlot = Opl3Tables.ReadChannelSlot(channelIndex);
            channel.Slotz[0] = Slots[localSlot];
            channel.Slotz[1] = Slots[localSlot + 3];
            channel.Slotz[0].Channel = channel;
            channel.Slotz[1].Channel = channel;
            channel.Slotz[0].Chip = this;
            channel.Slotz[1].Chip = this;
            channel.Pair = null;

            int mod9 = channelIndex % 9;
            channel.Pair = mod9 switch {
                < 3 => Channels[channelIndex + 3],
                < 6 => Channels[channelIndex - 3],
                _ => channel.Pair
            };

            channel.Chip = this;
            channel.Out[0] = ShortSignalSource.Zero;
            channel.Out[1] = ShortSignalSource.Zero;
            channel.Out[2] = ShortSignalSource.Zero;
            channel.Out[3] = ShortSignalSource.Zero;
            channel.ChannelType = ChannelType.TwoOp;
            channel.FNumber = 0;
            channel.Block = 0;
            channel.Feedback = 0;
            channel.Connection = 0;
            channel.Algorithm = 0;
            channel.KeyScaleValue = 0;
            channel.Cha = 0xffff;
            channel.Chb = 0xffff;
            channel.Chc = 0;
            channel.Chd = 0;
#if OPL_ENABLE_STEREOEXT
            channel.LeftPan = 0x10000;
            channel.RightPan = 0x10000;
#endif
            channel.ChannelNumber = (byte)channelIndex;
            ChannelSetupAlgorithm(channel);
        }
    }

    /* Original C: void OPL3_WriteReg(opl3_chip *chip, uint16_t reg, uint8_t v) */
    private void WriteRegisterInternal(ushort register, byte value) {
        byte high = (byte)((register >> 8) & 0x01);
        byte regm = (byte)(register & 0xff);

        int slotBase = high != 0 ? 18 : 0;
        int channelBase = high != 0 ? 9 : 0;

        switch (regm & 0xf0) {
            case 0x00:
                if (high != 0) {
                    switch (regm & 0x0f) {
                        case 0x04:
                            ChannelSet4Op(this, value);
                            break;
                        case 0x05:
                            NewM = (byte)(value & 0x01);
#if OPL_ENABLE_STEREOEXT
                            StereoExtension = (byte)((value >> 1) & 0x01);
#endif
                            break;
                    }
                } else {
                    if ((regm & 0x0f) == 0x08) {
                        Nts = (byte)((value >> 6) & 0x01);
                    }
                }

                break;

            case 0x20:
            case 0x30: {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0) {
                    SlotWrite20(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0x40:
            case 0x50: {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0) {
                    SlotWrite40(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0x60:
            case 0x70: {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0) {
                    SlotWrite60(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0x80:
            case 0x90: {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0) {
                    SlotWrite80(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0xe0:
            case 0xf0: {
                int slotIndex = Opl3Tables.ReadAddressDecodeSlot(regm & 0x1f);
                if (slotIndex >= 0) {
                    SlotWriteE0(Slots[slotBase + slotIndex], value);
                }

                break;
            }

            case 0xa0:
                if ((regm & 0x0f) < 9) {
                    ChannelWriteA0(Channels[channelBase + (regm & 0x0f)], value);
                }

                break;

            case 0xb0:
                if (regm == 0xbd && high == 0) {
                    Opl3Lfo.ConfigureDepth(this, value);
                    ChannelUpdateRhythm(this, value);
                } else if ((regm & 0x0f) < 9) {
                    Opl3Channel channel = Channels[channelBase + (regm & 0x0f)];
                    ChannelWriteB0(channel, value);
                    if ((value & 0x20) != 0) {
                        ChannelKeyOn(channel);
                    } else {
                        ChannelKeyOff(channel);
                    }
                }

                break;

            case 0xc0:
                if ((regm & 0x0f) < 9) {
                    ChannelWriteC0(Channels[channelBase + (regm & 0x0f)], value);
                }

                break;
#if OPL_ENABLE_STEREOEXT
            case 0xd0:
                if ((regm & 0x0f) < 9) {
                    ChannelWriteD0(Channels[channelBase + (regm & 0x0f)], value);
                }

                break;
#endif
        }
    }

    /* Original C: void OPL3_WriteRegBuffered(opl3_chip *chip, uint16_t reg, uint8_t v) */
    private void WriteRegisterBufferedInternal(ushort register, byte value) {
        int writebufLast = (int)WriteBufferLast;
        Opl3WriteBufferEntry entry = WriteBuffer[writebufLast];

        if ((entry.Register & 0x200) != 0) {
            WriteRegisterInternal((ushort)(entry.Register & 0x1ff), entry.Data);
            WriteBufferCurrent = (uint)((writebufLast + 1) % WriteBufferSize);
            WriteBufferSampleCounter = entry.Time;
        }

        entry.Register = (ushort)(register | 0x200);
        entry.Data = value;
        ulong time1 = WriteBufferLastTime + WriteBufferDelay;
        ulong time2 = WriteBufferSampleCounter;
        if (time1 < time2) {
            time1 = time2;
        }

        entry.Time = time1;
        WriteBufferLastTime = time1;
        WriteBufferLast = (uint)((writebufLast + 1) % WriteBufferSize);
    }

    /* Original C: void OPL3_Generate4ChStream(opl3_chip *chip, int16_t *sndptr1, int16_t *sndptr2, uint32_t numsamples) */
    private void Generate4ChStreamCore(Span<short> stream1, Span<short> stream2) {
        if ((stream1.Length & 1) != 0 || (stream2.Length & 1) != 0) {
            throw new ArgumentException("Stream buffers must contain an even number of elements.");
        }

        int frames = Math.Min(stream1.Length, stream2.Length) / 2;
        if (frames == 0) {
            return;
        }

        ref short leftReference = ref MemoryMarshal.GetReference(stream1);
        ref short rightReference = ref MemoryMarshal.GetReference(stream2);
        for (int i = 0; i < frames; i++) {
            int offset = i << 1;
            Generate4ChResampledCore(
                ref Unsafe.Add(ref leftReference, offset),
                ref Unsafe.Add(ref leftReference, offset + 1),
                ref Unsafe.Add(ref rightReference, offset),
                ref Unsafe.Add(ref rightReference, offset + 1));
        }
    }

    /* Original C: void OPL3_GenerateStream(opl3_chip *chip, int16_t *sndptr, uint32_t numsamples) */
    private void GenerateStreamCore(Span<short> stream) {
        if ((stream.Length & 1) != 0) {
            throw new ArgumentException("Stream buffer must contain an even number of elements.", nameof(stream));
        }

        int frames = stream.Length / 2;
        if (frames == 0) {
            return;
        }

        ref short destination = ref MemoryMarshal.GetReference(stream);
        short discardRearLeft = default;
        short discardRearRight = default;
        for (int sampleIndex = 0; sampleIndex < frames; sampleIndex++) {
            int offset = sampleIndex << 1;
            Generate4ChResampledCore(
                ref Unsafe.Add(ref destination, offset),
                ref Unsafe.Add(ref destination, offset + 1),
                ref discardRearLeft,
                ref discardRearRight);
        }
    }
}
