// SPDX-FileCopyrightText: 2020-2023, Andrea Zoppi
// SPDX-License-Identifier: BSD 2-Clause License

namespace Spice86.Libs.Sound.Devices.YM7128B;

internal static class Ym7128BHelpers {
    /*
    YM7128B_INLINE
    YM7128B_Tap YM7128B_RegisterToTap(YM7128B_Register data)
    */
    internal static ushort RegisterToTap(byte data) {
        byte index = (byte)(data & Ym7128BDatasheetSpecs.TapValueMask);
        return Ym7128BTables.Tap[index];
    }

    /*
    YM7128B_INLINE
    YM7128B_TapIdeal YM7128B_RegisterToTapIdeal(
        YM7128B_Register data,
        YM7128B_TapIdeal sample_rate
    )
    */
    internal static nuint RegisterToTapIdeal(byte data, nuint sampleRate) {
        byte index = (byte)(data & Ym7128BDatasheetSpecs.TapValueMask);
        return index * (sampleRate / 10) / (Ym7128BDatasheetSpecs.TapValueCount - 1);
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_RegisterToGainFixed(YM7128B_Register data)
    */
    internal static short RegisterToGainFixed(byte data) {
        byte index = (byte)(data & Ym7128BDatasheetSpecs.GainDataMask);
        return Ym7128BTables.GainFixed[index];
    }

    /*
    YM7128B_INLINE
    YM7128B_Float YM7128B_RegisterToGainFloat(YM7128B_Register data)
    */
    internal static float RegisterToGainFloat(byte data) {
        byte index = (byte)(data & Ym7128BDatasheetSpecs.GainDataMask);
        return Ym7128BTables.GainFloat[index];
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_RegisterToGainShort(YM7128B_Register data)
    */
    internal static short RegisterToGainShort(byte data) {
        byte index = (byte)(data & Ym7128BDatasheetSpecs.GainDataMask);
        return Ym7128BTables.GainShort[index];
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_RegisterToCoeffFixed(YM7128B_Register data)
    */
    internal static short RegisterToCoeffFixed(byte data) {
        byte raw = (byte)(data & Ym7128BDatasheetSpecs.CoeffValueMask);
        const int shift = Ym7128BImplementationSpecs.FixedBits - Ym7128BDatasheetSpecs.CoeffValueBits;
        return (short)(raw << shift);
    }

    /*
    YM7128B_INLINE
    YM7128B_Float YM7128B_RegisterToCoeffFloat(YM7128B_Register data)
    */
    internal static float RegisterToCoeffFloat(byte data) {
        short coeffFixed = RegisterToCoeffFixed(data);
        return coeffFixed * (1.0f / Ym7128BImplementationSpecs.GainMax);
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_RegisterToCoeffShort(YM7128B_Register data)
    */
    internal static short RegisterToCoeffShort(byte data) {
        byte raw = (byte)(data & Ym7128BDatasheetSpecs.CoeffValueMask);
        const int shift = Ym7128BImplementationSpecs.FixedBits - Ym7128BDatasheetSpecs.CoeffValueBits;
        return (short)(raw << shift);
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_ClampFixed(YM7128B_Accumulator signal)
    */
    internal static short ClampFixed(int signal) {
        if (signal < Ym7128BImplementationSpecs.FixedMin) {
            signal = Ym7128BImplementationSpecs.FixedMin;
        }

        if (signal > Ym7128BImplementationSpecs.FixedMax) {
            signal = Ym7128BImplementationSpecs.FixedMax;
        }

        return (short)(signal & Ym7128BImplementationSpecs.OperandMask);
    }

    /*
    YM7128B_INLINE
    YM7128B_Float YM7128B_ClampFloat(YM7128B_Float signal)
    */
    internal static float ClampFloat(float signal) {
        return signal switch {
            < -1.0f => -1.0f,
            > 1.0f => 1.0f,
            _ => signal
        };
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_ClampShort(YM7128B_Accumulator signal)
    */
    internal static short ClampShort(int signal) {
        return signal switch {
            < Ym7128BImplementationSpecs.FixedMin => Ym7128BImplementationSpecs.FixedMin,
            > Ym7128BImplementationSpecs.FixedMax => Ym7128BImplementationSpecs.FixedMax,
            _ => (short)signal
        };
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_ClampAddFixed(YM7128B_Fixed a, YM7128B_Fixed b)
    */
    internal static short ClampAddFixed(short a, short b) {
        int aa = a & Ym7128BImplementationSpecs.OperandMask;
        int bb = b & Ym7128BImplementationSpecs.OperandMask;
        int sum = aa + bb;
        return ClampFixed(sum);
    }

    /*
    YM7128B_INLINE
    YM7128B_Float YM7128B_ClampAddFloat(YM7128B_Float a, YM7128B_Float b)
    */
    internal static float ClampAddFloat(float a, float b) {
        return ClampFloat(a + b);
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_ClampAddShort(YM7128B_Fixed a, YM7128B_Fixed b)
    */
    internal static short ClampAddShort(short a, short b) {
        int sum = a + b;
        return ClampShort(sum);
    }

    /*
    YM7128B_INLINE
    YM7128B_Float YM7128B_AddFloat(YM7128B_Float a, YM7128B_Float b)
    */
    internal static float AddFloat(float a, float b) {
        return a + b;
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_MulFixed(YM7128B_Fixed a, YM7128B_Fixed b)
    */
    internal static short MulFixed(short a, short b) {
        int aa = a & Ym7128BImplementationSpecs.OperandMask;
        int bb = b & Ym7128BImplementationSpecs.OperandMask;
        int product = aa * bb;
        short value = (short)(product >> Ym7128BImplementationSpecs.FixedDecimals);
        return (short)(value & Ym7128BImplementationSpecs.OperandMask);
    }

    /*
    YM7128B_INLINE
    YM7128B_Float YM7128B_MulFloat(YM7128B_Float a, YM7128B_Float b)
    */
    internal static float MulFloat(float a, float b) {
        return a * b;
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_MulShort(YM7128B_Fixed a, YM7128B_Fixed b)
    */
    internal static short MulShort(short a, short b) {
        int product = a * b;
        return (short)(product >> Ym7128BImplementationSpecs.FixedDecimals);
    }

    /*
    YM7128B_INLINE
    YM7128B_Fixed YM7128B_OversamplerFixed_Process(
        YM7128B_OversamplerFixed* self,
        YM7128B_Fixed input
    )
    */
    internal static short OversamplerFixedProcess(Ym7128BOversamplerFixed self, short input) {
        ArgumentNullException.ThrowIfNull(self);

        int accum = 0;
        for (int i = 0; i < Ym7128BDatasheetSpecs.OversamplerLength; i++) {
            short sample = self.Buffer[i];
            self.Buffer[i] = input;
            input = sample;
            short kernel = Ym7128BTables.OversamplerFixedKernel[i];
            short oversampled = MulFixed(sample, kernel);
            accum += oversampled;
        }

        short clamped = ClampFixed(accum);
        return (short)(clamped & Ym7128BImplementationSpecs.SignalMask);
    }

    /*
    YM7128B_INLINE
    YM7128B_Float YM7128B_OversamplerFloat_Process(
        YM7128B_OversamplerFloat* self,
        YM7128B_Float input
    )
    */
    internal static float OversamplerFloatProcess(Ym7128BOversamplerFloat self, float input) {
        ArgumentNullException.ThrowIfNull(self);

        float accum = 0f;
        for (int i = 0; i < Ym7128BDatasheetSpecs.OversamplerLength; i++) {
            float sample = self.Buffer[i];
            self.Buffer[i] = input;
            input = sample;
            float kernel = Ym7128BTables.OversamplerFloatKernel[i];
            float oversampled = MulFloat(sample, kernel);
            accum += oversampled;
        }

        return ClampFloat(accum);
    }

    /*
    YM7128B_INLINE
    void YM7128B_OversamplerFixed_Clear(
        YM7128B_OversamplerFixed* self,
        YM7128B_Fixed input
    )
    */
    internal static void OversamplerFixedClear(Ym7128BOversamplerFixed oversampler, short input) {
        ArgumentNullException.ThrowIfNull(oversampler);
        oversampler.Buffer.AsSpan().Fill(input);
    }

    /*
    YM7128B_INLINE
    void YM7128B_OversamplerFixed_Reset(YM7128B_OversamplerFixed* self)
    */
    internal static void OversamplerFixedReset(Ym7128BOversamplerFixed oversampler) {
        OversamplerFixedClear(oversampler, 0);
    }

    /*
    YM7128B_INLINE
    void YM7128B_OversamplerFloat_Clear(
        YM7128B_OversamplerFloat* self,
        YM7128B_Float input
    )
    */
    internal static void OversamplerFloatClear(Ym7128BOversamplerFloat oversampler, float input) {
        ArgumentNullException.ThrowIfNull(oversampler);
        oversampler.Buffer.AsSpan().Fill(input);
    }

    /*
    YM7128B_INLINE
    void YM7128B_OversamplerFloat_Reset(YM7128B_OversamplerFloat* self)
    */
    internal static void OversamplerFloatReset(Ym7128BOversamplerFloat oversampler) {
        OversamplerFloatClear(oversampler, 0);
    }
}