// SPDX-FileCopyrightText: 2020-2023, Andrea Zoppi
// SPDX-License-Identifier: BSD 2-Clause License

namespace Spice86.Libs.Sound.Devices.YM7128B;

internal sealed partial class Ym7128BChip {
    private Ym7128BChipFloat Float { get; } = new();

    internal Ym7128BChipFloatProcessData FloatProcessData { get; } = new();

    internal void FloatCtor() {
        // No additional initialization required (struct fields already zeroed).
    }

    internal void FloatDtor() {
        // No heap-managed resources to release.
    }

    internal void FloatReset() {
        for (byte address = Ym7128BDatasheetSpecs.AddressMin; address <= Ym7128BDatasheetSpecs.AddressMax; address++) {
            FloatWrite(address, 0x00);
        }
    }

    internal void FloatStart() {
        Float.T0D = 0f;
        Float.Tail = 0;
        Array.Clear(Float.Buffer, 0, Float.Buffer.Length);
        foreach (Ym7128BOversamplerFloat oversampler in Float.Oversamplers) {
            Ym7128BHelpers.OversamplerFloatReset(oversampler);
        }
    }

    internal void FloatStop() {
        // No-op
    }

    internal void FloatProcess(Ym7128BChipFloatProcessData data) {
        ArgumentNullException.ThrowIfNull(data);

        float input = data.Inputs[(int)Ym7128BInputChannel.Mono];

        int t0 = Float.Tail + Float.Taps[0];
        int filterHead = t0 >= Ym7128BDatasheetSpecs.BufferLength ? t0 - Ym7128BDatasheetSpecs.BufferLength : t0;
        float filterT0 = Float.Buffer[filterHead];
        float filterD = Float.T0D;
        Float.T0D = filterT0;
        float filterC0 = Ym7128BHelpers.MulFloat(filterT0, Float.Gains[(int)Ym7128BRegister.C0]);
        float filterC1 = Ym7128BHelpers.MulFloat(filterD, Float.Gains[(int)Ym7128BRegister.C1]);
        float filterSum = Ym7128BHelpers.ClampAddFloat(filterC0, filterC1);
        float filterVc = Ym7128BHelpers.MulFloat(filterSum, Float.Gains[(int)Ym7128BRegister.Vc]);

        float inputVm = Ym7128BHelpers.MulFloat(input, Float.Gains[(int)Ym7128BRegister.Vm]);
        float inputSum = Ym7128BHelpers.ClampAddFloat(inputVm, filterVc);

        Float.Tail = Float.Tail != 0 ? (ushort)(Float.Tail - 1) : (ushort)(Ym7128BDatasheetSpecs.BufferLength - 1);
        Float.Buffer[Float.Tail] = inputSum;

        for (byte channel = 0; channel < (byte)Ym7128BOutputChannel.Count; channel++) {
            int gainBase = (int)Ym7128BRegister.Gl1 + (channel * Ym7128BDatasheetSpecs.GainLaneCount);
            float accum = 0f;

            for (byte tap = 1; tap < Ym7128BDatasheetSpecs.TapCount; tap++) {
                int t = Float.Tail + Float.Taps[tap];
                int head = t >= Ym7128BDatasheetSpecs.BufferLength ? t - Ym7128BDatasheetSpecs.BufferLength : t;
                float buffered = Float.Buffer[head];
                float g = Float.Gains[gainBase + tap - 1];
                float bufferedG = Ym7128BHelpers.MulFloat(buffered, g);
                accum += bufferedG;
            }

            float total = Ym7128BHelpers.ClampFloat(accum);
            float v = Float.Gains[(int)Ym7128BRegister.Vl + channel];
            float totalV = Ym7128BHelpers.MulFloat(total, v);

            Ym7128BOversamplerFloat oversampler = Float.Oversamplers[channel];
            float[,] outputs = data.Outputs;
            outputs[channel, 0] = Ym7128BHelpers.OversamplerFloatProcess(oversampler, totalV);
            for (byte j = 1; j < Ym7128BDatasheetSpecs.Oversampling; j++) {
                outputs[channel, j] = Ym7128BHelpers.OversamplerFloatProcess(oversampler, 0f);
            }
        }
    }

    internal byte FloatRead(byte address) {
        return address switch {
            < (byte)Ym7128BRegister.C0 => (byte)(Float.Registers[address] & Ym7128BDatasheetSpecs.GainDataMask),
            < (byte)Ym7128BRegister.T0 => (byte)(Float.Registers[address] & Ym7128BDatasheetSpecs.CoeffValueMask),
            < (byte)Ym7128BRegister.Count => (byte)(Float.Registers[address] & Ym7128BDatasheetSpecs.TapValueMask),
            _ => 0
        };
    }

    internal void FloatWrite(byte address, byte value) {
        switch (address) {
            case < (byte)Ym7128BRegister.C0:
                Float.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.GainDataMask);
                Float.Gains[address] = Ym7128BHelpers.RegisterToGainFloat(value);
                break;
            case < (byte)Ym7128BRegister.T0:
                Float.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.CoeffValueMask);
                Float.Gains[address] = Ym7128BHelpers.RegisterToCoeffFloat(value);
                break;
            case < (byte)Ym7128BRegister.Count:
                Float.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.TapValueMask);
                Float.Taps[address - (byte)Ym7128BRegister.T0] = Ym7128BHelpers.RegisterToTap(value);
                break;
        }
    }
}