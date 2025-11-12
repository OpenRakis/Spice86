// SPDX-FileCopyrightText: 2020-2023, Andrea Zoppi
// SPDX-License-Identifier: BSD 2-Clause License

namespace Spice86.Libs.Sound.Devices.YM7128B;

internal sealed partial class Ym7128BChip {
    internal Ym7128BChipFixed Fixed { get; } = new();

    internal Ym7128BChipFixedProcessData FixedProcessData { get; } = new();

    internal void FixedCtor() {
        // No heap allocations in C ctor; managed struct already zeroed.
    }

    internal void FixedDtor() {
        // No heap allocations; nothing to release.
    }

    internal void FixedReset() {
        for (byte address = Ym7128BDatasheetSpecs.AddressMin; address <= Ym7128BDatasheetSpecs.AddressMax; address++) {
            FixedWrite(address, 0x00);
        }
    }

    internal void FixedStart() {
        Fixed.T0D = 0;
        Fixed.Tail = 0;
        Array.Clear(Fixed.Buffer, 0, Fixed.Buffer.Length);
        foreach (Ym7128BOversamplerFixed oversampler in Fixed.Oversamplers) {
            Ym7128BHelpers.OversamplerFixedReset(oversampler);
        }
    }

    internal void FixedStop() {
        // No-op (MAYBE_UNUSED in C)
    }

    internal void FixedProcess(Ym7128BChipFixedProcessData data) {
        ArgumentNullException.ThrowIfNull(data);

        short input = data.Inputs[(int)Ym7128BInputChannel.Mono];
        short sample = (short)(input & Ym7128BImplementationSpecs.SignalMask);

        int t0 = Fixed.Tail + Fixed.Taps[0];
        int filterHead = t0 >= Ym7128BDatasheetSpecs.BufferLength ? t0 - Ym7128BDatasheetSpecs.BufferLength : t0;
        short filterT0 = Fixed.Buffer[filterHead];
        short filterD = Fixed.T0D;
        Fixed.T0D = filterT0;
        short filterC0 = Ym7128BHelpers.MulFixed(filterT0, Fixed.Gains[(int)Ym7128BRegister.C0]);
        short filterC1 = Ym7128BHelpers.MulFixed(filterD, Fixed.Gains[(int)Ym7128BRegister.C1]);
        short filterSum = Ym7128BHelpers.ClampAddFixed(filterC0, filterC1);
        short filterVc = Ym7128BHelpers.MulFixed(filterSum, Fixed.Gains[(int)Ym7128BRegister.Vc]);

        short inputVm = Ym7128BHelpers.MulFixed(sample, Fixed.Gains[(int)Ym7128BRegister.Vm]);
        short inputSum = Ym7128BHelpers.ClampAddFixed(inputVm, filterVc);

        Fixed.Tail = Fixed.Tail != 0 ? (ushort)(Fixed.Tail - 1) : (ushort)(Ym7128BDatasheetSpecs.BufferLength - 1);
        Fixed.Buffer[Fixed.Tail] = inputSum;

        for (byte channel = 0; channel < (byte)Ym7128BOutputChannel.Count; channel++) {
            int gainBase = (int)Ym7128BRegister.Gl1 + (channel * Ym7128BDatasheetSpecs.GainLaneCount);
            int accum = 0;

            for (byte tap = 1; tap < Ym7128BDatasheetSpecs.TapCount; tap++) {
                int t = Fixed.Tail + Fixed.Taps[tap];
                int head = t >= Ym7128BDatasheetSpecs.BufferLength ? t - Ym7128BDatasheetSpecs.BufferLength : t;
                short buffered = Fixed.Buffer[head];
                short g = Fixed.Gains[gainBase + tap - 1];
                short bufferedG = Ym7128BHelpers.MulFixed(buffered, g);
                accum += bufferedG;
            }

            short total = Ym7128BHelpers.ClampFixed(accum);
            short v = Fixed.Gains[(int)Ym7128BRegister.Vl + channel];
            short totalV = Ym7128BHelpers.MulFixed(total, v);

            Ym7128BOversamplerFixed oversampler = Fixed.Oversamplers[channel];
            data.Outputs[channel, 0] = Ym7128BHelpers.OversamplerFixedProcess(oversampler, totalV);
            for (byte j = 1; j < Ym7128BDatasheetSpecs.Oversampling; j++) {
                data.Outputs[channel, j] = Ym7128BHelpers.OversamplerFixedProcess(oversampler, 0);
            }
        }
    }

    internal byte FixedRead(byte address) {
        return address switch {
            < (byte)Ym7128BRegister.C0 => (byte)(Fixed.Registers[address] & Ym7128BDatasheetSpecs.GainDataMask),
            < (byte)Ym7128BRegister.T0 => (byte)(Fixed.Registers[address] & Ym7128BDatasheetSpecs.CoeffValueMask),
            < (byte)Ym7128BRegister.Count => (byte)(Fixed.Registers[address] & Ym7128BDatasheetSpecs.TapValueMask),
            _ => 0
        };
    }

    internal void FixedWrite(byte address, byte value) {
        switch (address) {
            case < (byte)Ym7128BRegister.C0:
                Fixed.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.GainDataMask);
                Fixed.Gains[address] = Ym7128BHelpers.RegisterToGainFixed(value);
                break;
            case < (byte)Ym7128BRegister.T0:
                Fixed.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.CoeffValueMask);
                Fixed.Gains[address] = Ym7128BHelpers.RegisterToCoeffFixed(value);
                break;
            case < (byte)Ym7128BRegister.Count:
                Fixed.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.TapValueMask);
                Fixed.Taps[address - (byte)Ym7128BRegister.T0] = Ym7128BHelpers.RegisterToTap(value);
                break;
        }
    }
}