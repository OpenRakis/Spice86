// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

using System.Runtime.CompilerServices;

/*
    Envelope generator
*/
internal static class Opl3Envelope {
    /*
        envelope_sinfunc envelope_sin[8]
    */
    private static readonly EnvelopeSinFunc[] EnvelopeSin = [
        EnvelopeCalcSin0,
        EnvelopeCalcSin1,
        EnvelopeCalcSin2,
        EnvelopeCalcSin3,
        EnvelopeCalcSin4,
        EnvelopeCalcSin5,
        EnvelopeCalcSin6,
        EnvelopeCalcSin7
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcExp(uint level) {
        if (level > 0x1fff) {
            level = 0x1fff;
        }

        int value = (Opl3Tables.ReadExp((int)(level & 0xff)) << 1) >> (int)(level >> 8);
        return (short)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin0(ushort phase, ushort envelope) {
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0) {
            neg = 0xffff;
        }

        ushort output = (phase & 0x100) != 0
            ? Opl3Tables.ReadLogSin((phase & 0xff) ^ 0xff)
            : Opl3Tables.ReadLogSin(phase & 0xff);

        ushort sample = (ushort)EnvelopeCalcExp((uint)(output + (envelope << 3)));
        return unchecked((short)(sample ^ neg));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin1(ushort phase, ushort envelope) {
        ushort output;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0) {
            output = 0x1000;
        } else if ((phase & 0x100) != 0) {
            output = Opl3Tables.ReadLogSin((phase & 0xff) ^ 0xff);
        } else {
            output = Opl3Tables.ReadLogSin(phase & 0xff);
        }

        return EnvelopeCalcExp((uint)(output + (envelope << 3)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin2(ushort phase, ushort envelope) {
        const ushort neg = 0xffff;
        phase &= 0x3ff;
        ushort output = (phase & 0x100) != 0
            ? Opl3Tables.ReadLogSin((phase & 0xff) ^ 0xff)
            : Opl3Tables.ReadLogSin(phase & 0xff);

        ushort sample = (ushort)EnvelopeCalcExp((uint)(output + (envelope << 3)));
        return unchecked((short)(sample ^ neg));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin3(ushort phase, ushort envelope) {
        ushort output;
        const ushort neg = 0xffff;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0) {
            output = 0x1000;
        } else if ((phase & 0x100) != 0) {
            output = Opl3Tables.ReadLogSin((phase & 0xff) ^ 0xff);
        } else {
            output = Opl3Tables.ReadLogSin(phase & 0xff);
        }

        ushort sample = (ushort)EnvelopeCalcExp((uint)(output + (envelope << 3)));
        return unchecked((short)(sample ^ neg));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin4(ushort phase, ushort envelope) {
        ushort output;
        const ushort neg = 0xffff;
        phase &= 0x3ff;
        if ((phase & 0x100) != 0) {
            output = (ushort)(((phase & 0xff) ^ 0xff) << 4);
        } else {
            output = (ushort)((phase & 0xff) << 4);
        }

        ushort sample = (ushort)EnvelopeCalcExp((uint)(output + (envelope << 3)));
        return unchecked((short)(sample ^ neg));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin5(ushort phase, ushort envelope) {
        ushort output;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0) {
            phase ^= 0x3ff;
        }

        if ((phase & 0x100) != 0) {
            output = (ushort)(((phase & 0xff) ^ 0xff) << 4);
        } else {
            output = (ushort)((phase & 0xff) << 4);
        }

        return EnvelopeCalcExp((uint)(output + (envelope << 3)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin6(ushort phase, ushort envelope) {
        ushort output;
        const ushort neg = 0xffff;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0) {
            output = 0x1000;
        } else if ((phase & 0x100) != 0) {
            output = Opl3Tables.ReadLogSin((phase & 0xff) ^ 0xff);
        } else {
            output = Opl3Tables.ReadLogSin(phase & 0xff);
        }

        phase = (ushort)((phase + 0x80) & 0x3ff);
        output += Opl3Tables.ReadLogSin(phase & 0xff);
        ushort sample = (ushort)EnvelopeCalcExp((uint)(output + (envelope << 3)));
        return unchecked((short)(sample ^ neg));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short EnvelopeCalcSin7(ushort phase, ushort envelope) {
        ushort output;
        phase &= 0x3ff;
        if ((phase & 0x200) != 0) {
            output = 0x1000;
        } else if ((phase & 0x100) != 0) {
            output = Opl3Tables.ReadLogSin((phase & 0xff) ^ 0xff);
        } else {
            output = Opl3Tables.ReadLogSin(phase & 0xff);
        }

        phase = (ushort)((phase + 0x80) & 0x3ff);
        output += Opl3Tables.ReadLogSin(phase & 0xff);
        return EnvelopeCalcExp((uint)(output + (envelope << 3)));
    }

    internal static void EnvelopeUpdateKsl(Opl3Operator slot) {
        Opl3Channel channel = slot.Channel ?? throw new InvalidOperationException("Channel not assigned.");
        short value = (short)((Opl3Tables.ReadKeyScaleLevel(channel.FNumber >> 6) << 2)
                              - ((0x08 - channel.Block) << 5));
        if (value < 0) {
            value = 0;
        }

        slot.EffectiveKeyScaleLevel = (byte)value;
    }

    internal static void EnvelopeCalc(Opl3Operator slot) {
        Opl3Chip chip = slot.Chip ?? throw new InvalidOperationException("Chip not assigned.");
        Opl3Channel channel = slot.Channel ?? throw new InvalidOperationException("Channel not assigned.");

        byte regRate = 0;
        byte shift = 0;
        int egIncrement = 0;
        byte reset = 0;

        byte tremoloValue = slot.TremoloEnabled ? chip.Tremolo : (byte)0;
        slot.EnvelopeGeneratorLevel = (ushort)(slot.EnvelopeGeneratorOutput + (slot.RegTotalLevel << 2)
                                                                            + (slot.EffectiveKeyScaleLevel >>
                                                                               Opl3Tables.ReadKeyScaleShift(
                                                                                   slot.RegKeyScaleLevel)) +
                                                                            tremoloValue);

        if (slot.RegKeyState != 0 && slot.EnvelopeGeneratorState == (byte)EnvelopeGeneratorStage.Release) {
            reset = 1;
            regRate = slot.RegAttackRate;
        } else {
            switch ((EnvelopeGeneratorStage)slot.EnvelopeGeneratorState) {
                case EnvelopeGeneratorStage.Attack:
                    regRate = slot.RegAttackRate;
                    break;
                case EnvelopeGeneratorStage.Decay:
                    regRate = slot.RegDecayRate;
                    break;
                case EnvelopeGeneratorStage.Sustain:
                    if (slot.RegOperatorType == 0) {
                        regRate = slot.RegReleaseRate;
                    }

                    break;
                case EnvelopeGeneratorStage.Release:
                    regRate = slot.RegReleaseRate;
                    break;
            }
        }

        slot.RegPhaseResetRequest = reset;
        byte keyScale = (byte)(channel.KeyScaleValue >> ((slot.RegKeyScaleRate ^ 1) << 1));
        byte nonZero = (byte)(regRate != 0 ? 1 : 0);
        byte rate = (byte)(keyScale + (regRate << 2));
        byte rateHi = (byte)(rate >> 2);
        byte rateLo = (byte)(rate & 0x03);
        if ((rateHi & 0x10) != 0) {
            rateHi = 0x0f;
        }

        byte egShift = (byte)(rateHi + chip.EgAdd);
        if (nonZero != 0) {
            if (rateHi < 12) {
                if (chip.EgState != 0) {
                    shift = egShift switch {
                        12 => 1,
                        13 => (byte)((rateLo >> 1) & 0x01),
                        14 => (byte)(rateLo & 0x01),
                        _ => shift
                    };
                }
            } else {
                shift = (byte)((rateHi & 0x03) + Opl3Tables.EgIncrementSteps[rateLo, chip.EgTimerLow]);
                if ((shift & 0x04) != 0) {
                    shift = 0x03;
                }

                if (shift == 0) {
                    shift = chip.EgState;
                }
            }
        }

        ushort egRout = slot.EnvelopeGeneratorOutput;
        byte egOff = 0;

        if (reset != 0 && rateHi == 0x0f) {
            egRout = 0x00;
        }

        if ((slot.EnvelopeGeneratorOutput & 0x1f8) == 0x1f8) {
            egOff = 1;
        }

        if (slot.EnvelopeGeneratorState != (byte)EnvelopeGeneratorStage.Attack && reset == 0 && egOff != 0) {
            egRout = 0x1ff;
        }

        switch ((EnvelopeGeneratorStage)slot.EnvelopeGeneratorState) {
            case EnvelopeGeneratorStage.Attack:
                if (slot.EnvelopeGeneratorOutput == 0) {
                    slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Decay;
                } else if (slot.RegKeyState != 0 && shift > 0 && rateHi != 0x0f) {
                    egIncrement = ~slot.EnvelopeGeneratorOutput >> (4 - shift);
                }

                break;

            case EnvelopeGeneratorStage.Decay:
                if (slot.EnvelopeGeneratorOutput >> 4 == slot.RegSustainLevel) {
                    slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Sustain;
                } else if (egOff == 0 && reset == 0 && shift > 0) {
                    egIncrement = 1 << (shift - 1);
                }

                break;

            case EnvelopeGeneratorStage.Sustain:
            case EnvelopeGeneratorStage.Release:
                if (egOff == 0 && reset == 0 && shift > 0) {
                    egIncrement = 1 << (shift - 1);
                }

                break;
        }

        slot.EnvelopeGeneratorOutput = (ushort)((egRout + egIncrement) & 0x1ff);

        if (reset != 0) {
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Attack;
        }

        if (slot.RegKeyState == 0) {
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;
        }
    }

    internal static void EnvelopeKeyOn(Opl3Operator slot, EnvelopeKeyType type) {
        slot.RegKeyState = (byte)(slot.RegKeyState | (byte)type);
    }

    internal static void EnvelopeKeyOff(Opl3Operator slot, EnvelopeKeyType type) {
        slot.RegKeyState = (byte)(slot.RegKeyState & ~(byte)type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static short GenerateWaveform(Opl3Operator slot) {
        int index = slot.RegWaveformSelect & 0x07;
        short mod = slot.ModulationSource.Read();
        ushort phase = (ushort)unchecked(slot.PhaseGeneratorOutput + (ushort)mod);
        return EnvelopeSin[index](phase, slot.EnvelopeGeneratorLevel);
    }

    private delegate short EnvelopeSinFunc(ushort phase, ushort envelope);
}