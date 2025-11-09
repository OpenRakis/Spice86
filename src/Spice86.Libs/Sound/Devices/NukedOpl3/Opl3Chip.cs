// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

public sealed partial class Opl3Chip {
    private const int WriteBufferSize = 1024;
    private const int WriteBufferDelay = 2;
    private const int ResampleFractionBits = 10;

    private Opl3Channel[] Channels { get; } = new Opl3Channel[18];
    public Opl3Operator[] Slots { get; } = new Opl3Operator[36];
    internal ushort Timer;
    internal ulong EgTimer;
    internal byte EgTimerRem;
    internal byte EgState;
    internal byte EgAdd;
    internal byte EgTimerLow;
    internal byte NewM;
    internal byte Nts;
    internal byte Rhythm;
    internal byte VibratoPosition;
    internal byte VibratoShift;
    internal byte Tremolo;
    internal byte TremoloPosition;
    internal byte TremoloShift;
    internal uint Noise;
    internal short ZeroMod;
    private int[] MixBuffer { get; } = new int[4];
    internal byte RhythmHihatBit2;
    internal byte RhythmHihatBit3;
    internal byte RhythmHihatBit7;
    internal byte RhythmHihatBit8;
    internal byte RhythmTomBit3;
    internal byte RhythmTomBit5;
#if OPL_ENABLE_STEREOEXT
    internal byte StereoExtension;
#endif
    internal int RateRatio;
    internal int SampleCounter;
    private short[] OldSamples { get; } = new short[4];
    private short[] Samples { get; } = new short[4];
    internal ulong WriteBufferSampleCounter;
    internal uint WriteBufferCurrent;
    internal uint WriteBufferLast;
    internal ulong WriteBufferLastTime;
    private Opl3WriteBufferEntry[] WriteBuffer { get; } = new Opl3WriteBufferEntry[WriteBufferSize];

    public Opl3Chip() {
        for (int i = 0; i < Channels.Length; i++) {
            Channels[i] = new Opl3Channel {
                Chip = this,
                ChannelNumber = (byte)i,
                ChannelType = ChannelType.TwoOp
            };
            for (int j = 0; j < Channels[i].Out.Length; j++) {
                Channels[i].Out[j] = ShortSignalSource.Zero;
            }
        }

        for (int i = 0; i < Slots.Length; i++) {
            Slots[i] = new Opl3Operator {
                Chip = this,
                SlotIndex = (byte)i,
                ModulationSource = ShortSignalSource.Zero,
                TremoloEnabled = false
            };
        }

        for (int i = 0; i < WriteBuffer.Length; i++) {
            WriteBuffer[i] = new Opl3WriteBufferEntry();
        }
    }


    /* Original C: void OPL3_Reset(opl3_chip *chip, uint32_t samplerate); */
    public void Reset(uint sampleRate) {
        ResetInternal(sampleRate);
    }

    /* Original C: void OPL3_WriteReg(opl3_chip *chip, uint16_t reg, uint8_t v); */
    public void WriteRegister(ushort register, byte value) {
        WriteRegisterInternal(register, value);
    }

    /* Original C: void OPL3_WriteRegBuffered(opl3_chip *chip, uint16_t reg, uint8_t v); */
    public void WriteRegisterBuffered(ushort register, byte value) {
        WriteRegisterBufferedInternal(register, value);
    }

    /* Original C: void OPL3_Generate4Ch(opl3_chip *chip, int16_t *buf4); */
    public void Generate4Channels(Span<short> buffer) {
        Generate4ChCore(buffer);
    }

    /* Original C: void OPL3_Generate(opl3_chip *chip, int16_t *buf); */
    public void Generate(Span<short> buffer) {
        GenerateCore(buffer);
    }

    /* Original C: void OPL3_Generate4ChResampled(opl3_chip *chip, int16_t *buf4); */
    public void Generate4ChannelsResampled(Span<short> buffer) {
        Generate4ChResampledCore(buffer);
    }

    /* Original C: void OPL3_GenerateResampled(opl3_chip *chip, int16_t *buf); */
    public void GenerateResampled(Span<short> buffer) {
        GenerateResampledCore(buffer);
    }

    /* Original C: void OPL3_Generate4ChStream(opl3_chip *chip, int16_t *sndptr1, int16_t *sndptr2, uint32_t numsamples); */
    public void Generate4ChannelStream(Span<short> stream1, Span<short> stream2) {
        Generate4ChStreamCore(stream1, stream2);
    }

    /* Original C: void OPL3_GenerateStream(opl3_chip *chip, int16_t *sndptr, uint32_t numsamples); */
    public void GenerateStream(Span<short> stream) {
        GenerateStreamCore(stream);
    }

    internal ulong GetWriteBufferSampleCounter() {
        return WriteBufferSampleCounter;
    }

    internal ulong? PeekNextBufferedWriteSample() {
        Opl3WriteBufferEntry entry = WriteBuffer[(int)WriteBufferCurrent];
        return (entry.Register & 0x200) != 0 ? entry.Time : null;
    }

    internal void ProcessWriteBufferUntil(ulong inclusiveSampleIndex) {
        if (WriteBufferSampleCounter > inclusiveSampleIndex) {
            return;
        }

        do {
            while (true) {
                Opl3WriteBufferEntry entry = WriteBuffer[(int)WriteBufferCurrent];
                if (entry.Time > WriteBufferSampleCounter) {
                    break;
                }

                if ((entry.Register & 0x200) == 0) {
                    break;
                }

                ushort register = (ushort)(entry.Register & 0x1ff);
                entry.Register = register;
                WriteRegisterInternal(register, entry.Data);
                WriteBufferCurrent = (WriteBufferCurrent + 1) % WriteBufferSize;
            }

            WriteBufferSampleCounter++;
        } while (WriteBufferSampleCounter <= inclusiveSampleIndex);
    }
}

internal enum ChannelType : byte {
    TwoOp = 0,
    FourOp = 1,
    FourOpPair = 2,
    Drum = 3
}

internal enum EnvelopeKeyType : byte {
    Normal = 0x01,
    Drum = 0x02
}

public enum EnvelopeGeneratorStage : byte {
    Attack = 0,
    Decay = 1,
    Sustain = 2,
    Release = 3
}

internal sealed class Opl3WriteBufferEntry {
    internal byte Data;
    internal ushort Register;
    internal ulong Time;
}