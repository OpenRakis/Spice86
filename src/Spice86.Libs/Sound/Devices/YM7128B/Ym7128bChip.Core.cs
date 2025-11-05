// SPDX-FileCopyrightText: 2020-2023, Andrea Zoppi
// SPDX-License-Identifier: BSD 2-Clause License

namespace Spice86.Libs.Sound.Devices.YM7128B;

internal sealed partial class Ym7128BChip {
    internal Ym7128BChip() {
        ChipIdealCtor(Ideal);
        FixedCtor();
        FloatCtor();
        ShortCtor();
    }

    internal void IdealCtor() {
        ChipIdealCtor(Ideal);
    }

    internal void IdealDtor() {
        ChipIdealDtor(Ideal);
    }

    internal void IdealReset() {
        ChipIdealReset(Ideal);
    }

    internal void IdealStart() {
        ChipIdealStart(Ideal);
    }

    internal void IdealStop() {
        ChipIdealStop(Ideal);
    }

    internal void IdealSetup(nuint sampleRate) {
        ChipIdealSetup(Ideal, sampleRate);
    }

    internal void IdealWrite(byte address, byte value) {
        ChipIdealWrite(Ideal, address, value);
    }

    internal byte IdealRead(byte address) {
        return ChipIdealRead(Ideal, address);
    }

    internal void IdealProcess(Ym7128BChipIdealProcessData data) {
        ChipIdealProcess(Ideal, data);
    }

    internal void FixedResetShim() {
        FixedReset();
    }

    internal void FixedStartShim() {
        FixedStart();
    }

    internal void FixedStopShim() {
        FixedStop();
    }

    internal void FixedProcessShim(Ym7128BChipFixedProcessData data) {
        FixedProcess(data);
    }

    internal byte FixedReadShim(byte address) {
        return FixedRead(address);
    }

    internal void FixedWriteShim(byte address, byte value) {
        FixedWrite(address, value);
    }

    internal void FloatResetShim() {
        FloatReset();
    }

    internal void FloatStartShim() {
        FloatStart();
    }

    internal void FloatStopShim() {
        FloatStop();
    }

    internal void FloatProcessShim(Ym7128BChipFloatProcessData data) {
        FloatProcess(data);
    }

    internal byte FloatReadShim(byte address) {
        return FloatRead(address);
    }

    internal void FloatWriteShim(byte address, byte value) {
        FloatWrite(address, value);
    }

    internal void ShortResetShim() {
        ShortReset();
    }

    internal void ShortStartShim() {
        ShortStart();
    }

    internal void ShortStopShim() {
        ShortStop();
    }

    internal void ShortSetupShim(nuint sampleRate) {
        ShortSetup(sampleRate);
    }

    internal void ShortProcessShim(Ym7128BChipShortProcessData data) {
        ShortProcess(data);
    }

    internal byte ShortReadShim(byte address) {
        return ShortRead(address);
    }

    internal void ShortWriteShim(byte address, byte value) {
        ShortWrite(address, value);
    }

    private static void ChipIdealCtor(Ym7128BChipIdeal chip) {
        ArgumentNullException.ThrowIfNull(chip);
        chip.Buffer = null;
        chip.Length = 0;
        chip.SampleRate = 0;
        chip.T0D = 0;
        chip.Tail = 0;
        Array.Clear(chip.Registers, 0, chip.Registers.Length);
        Array.Clear(chip.Gains, 0, chip.Gains.Length);
        Array.Clear(chip.Taps, 0, chip.Taps.Length);
    }

    private static void ChipIdealDtor(Ym7128BChipIdeal chip) {
        ArgumentNullException.ThrowIfNull(chip);
        chip.Buffer = null;
        chip.Length = 0;
    }

    private static void ChipIdealReset(Ym7128BChipIdeal chip) {
        ArgumentNullException.ThrowIfNull(chip);

        for (byte address = Ym7128BDatasheetSpecs.AddressMin; address <= Ym7128BDatasheetSpecs.AddressMax; address++) {
            ChipIdealWrite(chip, address, 0);
        }
    }

    private static void ChipIdealStart(Ym7128BChipIdeal chip) {
        ArgumentNullException.ThrowIfNull(chip);

        chip.T0D = 0;
        chip.Tail = 0;

        if (chip.Buffer != null) {
            Array.Clear(chip.Buffer, 0, chip.Buffer.Length);
        }
    }

    private static void ChipIdealStop(Ym7128BChipIdeal chip) {
        ArgumentNullException.ThrowIfNull(chip);
    }

    private static void ChipIdealProcess(Ym7128BChipIdeal chip, Ym7128BChipIdealProcessData data) {
        ArgumentNullException.ThrowIfNull(chip);
        ArgumentNullException.ThrowIfNull(data);

        if (chip.Buffer == null || chip.Length == 0) {
            return;
        }

        float input = data.Inputs[(int)Ym7128BInputChannel.Mono];

        nuint t0 = chip.Tail + chip.Taps[0];
        nuint filterHead = t0 >= chip.Length ? t0 - chip.Length : t0;
        float filterT0 = chip.Buffer[(int)filterHead];
        float filterD = chip.T0D;
        chip.T0D = filterT0;
        float filterC0 = Ym7128BHelpers.MulFloat(filterT0, chip.Gains[(int)Ym7128BRegister.C0]);
        float filterC1 = Ym7128BHelpers.MulFloat(filterD, chip.Gains[(int)Ym7128BRegister.C1]);
        float filterSum = Ym7128BHelpers.AddFloat(filterC0, filterC1);
        float filterVc = Ym7128BHelpers.MulFloat(filterSum, chip.Gains[(int)Ym7128BRegister.Vc]);

        float inputVm = Ym7128BHelpers.MulFloat(input, chip.Gains[(int)Ym7128BRegister.Vm]);
        float inputSum = Ym7128BHelpers.AddFloat(inputVm, filterVc);

        chip.Tail = chip.Tail != 0 ? chip.Tail - 1 : chip.Length - 1;
        chip.Buffer[(int)chip.Tail] = inputSum;

        for (byte channel = 0; channel < (byte)Ym7128BOutputChannel.Count; channel++) {
            int gainBase = (int)Ym7128BRegister.Gl1 + (channel * Ym7128BDatasheetSpecs.GainLaneCount);
            float accum = 0;

            for (byte tap = 1; tap < Ym7128BDatasheetSpecs.TapCount; tap++) {
                nuint t = chip.Tail + chip.Taps[tap];
                nuint head = t >= chip.Length ? t - chip.Length : t;
                float buffered = chip.Buffer[(int)head];
                float g = chip.Gains[gainBase + tap - 1];
                float bufferedG = Ym7128BHelpers.MulFloat(buffered, g);
                accum += bufferedG;
            }

            float total = accum;
            float v = chip.Gains[(int)Ym7128BRegister.Vl + channel];
            float totalV = Ym7128BHelpers.MulFloat(total, v);
            float oversampled = Ym7128BHelpers.MulFloat(totalV, 1.0f / Ym7128BDatasheetSpecs.Oversampling);
            data.Outputs[channel] = oversampled;
        }
    }

    private static byte ChipIdealRead(Ym7128BChipIdeal chip, byte address) {
        ArgumentNullException.ThrowIfNull(chip);

        return address switch {
            < (byte)Ym7128BRegister.C0 => (byte)(chip.Registers[address] & Ym7128BDatasheetSpecs.GainDataMask),
            < (byte)Ym7128BRegister.T0 => (byte)(chip.Registers[address] & Ym7128BDatasheetSpecs.CoeffValueMask),
            < (byte)Ym7128BRegister.Count => (byte)(chip.Registers[address] & Ym7128BDatasheetSpecs.TapValueMask),
            _ => 0
        };
    }

    private static void ChipIdealWrite(Ym7128BChipIdeal chip, byte address, byte value) {
        ArgumentNullException.ThrowIfNull(chip);

        switch (address) {
            case < (byte)Ym7128BRegister.C0:
                chip.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.GainDataMask);
                chip.Gains[address] = Ym7128BHelpers.RegisterToGainFloat(value);
                break;
            case < (byte)Ym7128BRegister.T0:
                chip.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.CoeffValueMask);
                chip.Gains[address] = Ym7128BHelpers.RegisterToCoeffFloat(value);
                break;
            case < (byte)Ym7128BRegister.Count:
                chip.Registers[address] = (byte)(value & Ym7128BDatasheetSpecs.TapValueMask);
                chip.Taps[address - (byte)Ym7128BRegister.T0] =
                    Ym7128BHelpers.RegisterToTapIdeal(value, chip.SampleRate);
                break;
        }
    }

    private static void ChipIdealSetup(Ym7128BChipIdeal chip, nuint sampleRate) {
        ArgumentNullException.ThrowIfNull(chip);

        if (chip.SampleRate == sampleRate && chip.Buffer != null) {
            return;
        }

        chip.SampleRate = sampleRate;
        chip.Buffer = null;
        chip.Length = 0;

        if (sampleRate < 10) {
            return;
        }

        nuint length = (sampleRate / 10) + 1;
        chip.Length = length;
        chip.Buffer = new float[(int)length];

        for (byte i = 0; i < Ym7128BDatasheetSpecs.TapCount; i++) {
            byte data = chip.Registers[i + (int)Ym7128BRegister.T0];
            chip.Taps[i] = Ym7128BHelpers.RegisterToTapIdeal(data, chip.SampleRate);
        }
    }
}