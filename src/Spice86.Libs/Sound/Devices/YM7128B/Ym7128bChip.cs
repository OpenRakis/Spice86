namespace Spice86.Libs.Sound.Devices.YM7128B;

/*
BSD 2-Clause License

Copyright (c) 2020-2023, Andrea Zoppi
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

internal sealed partial class Ym7128BChip {
    private Ym7128BChipIdeal Ideal { get; } = new();
}

internal enum Ym7128BRegister : byte {
    Gl1 = 0,
    Gl2,
    Gl3,
    Gl4,
    Gl5,
    Gl6,
    Gl7,
    Gl8,
    Gr1,
    Gr2,
    Gr3,
    Gr4,
    Gr5,
    Gr6,
    Gr7,
    Gr8,
    Vm,
    Vc,
    Vl,
    Vr,
    C0,
    C1,
    T0,
    T1,
    T2,
    T3,
    T4,
    T5,
    T6,
    T7,
    T8,
    Count
}

internal enum Ym7128BInputChannel : byte {
    Mono = 0,
    Count
}

internal enum Ym7128BOutputChannel : byte {
    Left = 0,
    Right,
    Count
}

internal static class Ym7128BDatasheetSpecs {
    internal const int ClockRate = 7_159_090;
    internal const int WriteRate = ClockRate / 8 / (8 + 1 + 8 + 1);
    internal const int InputRate = (ClockRate + (304 / 2)) / 304;
    internal const int Oversampling = 2;
    internal const int OutputRate = InputRate * Oversampling;
    internal const int OversamplerLength = 19;
    internal const byte AddressMin = 0;
    internal const byte AddressMax = (byte)(Ym7128BRegister.Count - 1);
    internal const int BufferLength = (InputRate / 10) + 1;
    internal const int TapCount = 9;
    internal const int TapValueBits = 5;
    internal const int TapValueCount = 1 << TapValueBits;
    internal const int TapValueMask = TapValueCount - 1;
    internal const int GainLaneCount = 8;
    internal const int GainDataBits = 6;
    internal const int GainDataCount = 1 << GainDataBits;
    internal const int GainDataMask = GainDataCount - 1;
    internal const int GainDataSign = 1 << (GainDataBits - 1);
    internal const int CoeffCount = 2;
    internal const int CoeffValueBits = 6;
    internal const int CoeffValueCount = 1 << CoeffValueBits;
    internal const int CoeffValueMask = CoeffValueCount - 1;
}

internal static class Ym7128BImplementationSpecs {
    internal const int FixedBits = sizeof(short) * 8;
    internal const int FixedMask = (1 << FixedBits) - 1;
    internal const int FixedDecimals = FixedBits - 1;
    internal const int FixedRounding = 1 << (FixedDecimals - 1);
    internal const int FixedMax = (1 << FixedDecimals) - 1;
    internal const int FixedMin = -FixedMax;
    internal const int SignalBits = 14;
    internal const int SignalClearBits = FixedBits - SignalBits;
    internal const int SignalClearMask = (1 << SignalClearBits) - 1;
    internal const int SignalMask = FixedMask - SignalClearMask;
    internal const int SignalDecimals = SignalBits - 1;
    internal const int OperandBits = FixedBits;
    internal const int OperandClearBits = FixedBits - OperandBits;
    internal const int OperandClearMask = (1 << OperandClearBits) - 1;
    internal const int OperandMask = FixedMask - OperandClearMask;
    internal const int OperandDecimals = OperandBits - 1;
    internal const int GainBits = 12;
    internal const int GainClearBits = FixedBits - GainBits;
    internal const int GainClearMask = (1 << GainClearBits) - 1;
    internal const int GainMask = FixedMask - GainClearMask;
    internal const int GainDecimals = GainBits - 1;
    internal const int GainMax = (1 << (FixedBits - 1)) - 1;
    internal const int GainMin = -GainMax;
    internal const int CoeffBits = GainBits;
    internal const int CoeffClearBits = FixedBits - CoeffBits;
    internal const int CoeffClearMask = (1 << CoeffClearBits) - 1;
    internal const int CoeffMask = FixedMask - CoeffClearMask;
    internal const int CoeffDecimals = CoeffBits - 1;
}

/*
typedef struct YM7128B_OversamplerFixed
{
    YM7128B_Fixed buffer_[YM7128B_Oversampler_Length];
} YM7128B_OversamplerFixed;
*/
internal sealed class Ym7128BOversamplerFixed {
    internal short[] Buffer { get; } = new short[Ym7128BDatasheetSpecs.OversamplerLength];
}

/*
typedef struct YM7128B_OversamplerFloat
{
    YM7128B_Float buffer_[YM7128B_Oversampler_Length];
} YM7128B_OversamplerFloat;
*/
internal sealed class Ym7128BOversamplerFloat {
    internal float[] Buffer { get; } = new float[Ym7128BDatasheetSpecs.OversamplerLength];
}

/*
typedef struct YM7128B_ChipIdeal
{
    YM7128B_Register regs_[YM7128B_Reg_Count];
    YM7128B_Float gains_[YM7128B_Reg_T0];
    YM7128B_TapIdeal taps_[YM7128B_Tap_Count];
    YM7128B_Float t0_d_;
    YM7128B_TapIdeal tail_;
    YM7128B_Float* buffer_;
    YM7128B_TapIdeal length_;
    YM7128B_TapIdeal sample_rate_;
} YM7128B_ChipIdeal;
*/
internal sealed class Ym7128BChipIdeal {
    internal float[]? Buffer;
    internal nuint Length;
    internal nuint SampleRate;
    internal float T0D;
    internal nuint Tail;
    internal byte[] Registers { get; } = new byte[(int)Ym7128BRegister.Count];
    internal float[] Gains { get; } = new float[(int)Ym7128BRegister.T0];
    internal nuint[] Taps { get; } = new nuint[Ym7128BDatasheetSpecs.TapCount];
}

/*
typedef struct YM7128B_ChipIdeal_Process_Data
{
    YM7128B_Float inputs[YM7128B_InputChannel_Count];
    YM7128B_Float outputs[YM7128B_OutputChannel_Count];
} YM7128B_ChipIdeal_Process_Data;
*/
internal sealed class Ym7128BChipIdealProcessData {
    internal float[] Inputs { get; } = new float[(int)Ym7128BInputChannel.Count];
    internal float[] Outputs { get; } = new float[(int)Ym7128BOutputChannel.Count];
}

/*
typedef struct YM7128B_ChipFixed
{
    YM7128B_Register regs_[YM7128B_Reg_Count];
    YM7128B_Fixed gains_[YM7128B_Reg_T0];
    YM7128B_Tap taps_[YM7128B_Tap_Count];
    YM7128B_Fixed t0_d_;
    YM7128B_Tap tail_;
    YM7128B_Fixed buffer_[YM7128B_Buffer_Length];
    YM7128B_OversamplerFixed oversampler_[YM7128B_OutputChannel_Count];
} YM7128B_ChipFixed;
*/
internal sealed class Ym7128BChipFixed {
    internal short T0D;
    internal ushort Tail;
    internal byte[] Registers { get; } = new byte[(int)Ym7128BRegister.Count];
    internal short[] Gains { get; } = new short[(int)Ym7128BRegister.T0];
    internal ushort[] Taps { get; } = new ushort[Ym7128BDatasheetSpecs.TapCount];
    internal short[] Buffer { get; } = new short[Ym7128BDatasheetSpecs.BufferLength];

    internal Ym7128BOversamplerFixed[] Oversamplers { get; } = [
        new(),
        new()
    ];
}

/*
typedef struct YM7128B_ChipFixed_Process_Data
{
    YM7128B_Fixed inputs[YM7128B_InputChannel_Count];
    YM7128B_Fixed outputs[YM7128B_OutputChannel_Count][YM7128B_Oversampling];
} YM7128B_ChipFixed_Process_Data;
*/
internal sealed class Ym7128BChipFixedProcessData {
    internal short[] Inputs { get; } = new short[(int)Ym7128BInputChannel.Count];
    internal short[,] Outputs { get; } = new short[(int)Ym7128BOutputChannel.Count, Ym7128BDatasheetSpecs.Oversampling];
}

/*
typedef struct YM7128B_ChipFloat
{
    YM7128B_Register regs_[YM7128B_Reg_Count];
    YM7128B_Float gains_[YM7128B_Reg_T0];
    YM7128B_Tap taps_[YM7128B_Tap_Count];
    YM7128B_Float t0_d_;
    YM7128B_Tap tail_;
    YM7128B_Float buffer_[YM7128B_Buffer_Length];
    YM7128B_OversamplerFloat oversampler_[YM7128B_OutputChannel_Count];
} YM7128B_ChipFloat;
*/
internal sealed class Ym7128BChipFloat {
    internal float T0D;
    internal ushort Tail;
    internal byte[] Registers { get; } = new byte[(int)Ym7128BRegister.Count];
    internal float[] Gains { get; } = new float[(int)Ym7128BRegister.T0];
    internal ushort[] Taps { get; } = new ushort[Ym7128BDatasheetSpecs.TapCount];
    internal float[] Buffer { get; } = new float[Ym7128BDatasheetSpecs.BufferLength];

    internal Ym7128BOversamplerFloat[] Oversamplers { get; } = [
        new(),
        new()
    ];
}

/*
typedef struct YM7128B_ChipFloat_Process_Data
{
    YM7128B_Float inputs[YM7128B_InputChannel_Count];
    YM7128B_Float outputs[YM7128B_OutputChannel_Count][YM7128B_Oversampling];
} YM7128B_ChipFloat_Process_Data;
*/
internal sealed class Ym7128BChipFloatProcessData {
    internal float[] Inputs { get; } = new float[(int)Ym7128BInputChannel.Count];
    internal float[,] Outputs { get; } = new float[(int)Ym7128BOutputChannel.Count, Ym7128BDatasheetSpecs.Oversampling];
}

/*
typedef struct YM7128B_ChipShort
{
    YM7128B_Register regs_[YM7128B_Reg_Count];
    YM7128B_Fixed gains_[YM7128B_Reg_T0];
    YM7128B_TapIdeal taps_[YM7128B_Tap_Count];
    YM7128B_Fixed t0_d_;
    YM7128B_TapIdeal tail_;
    YM7128B_Fixed* buffer_;
    YM7128B_TapIdeal length_;
    YM7128B_TapIdeal sample_rate_;
} YM7128B_ChipShort;
*/
internal sealed class Ym7128BChipShort {
    internal short[]? Buffer;
    internal nuint Length;
    internal nuint SampleRate;
    internal short T0D;
    internal nuint Tail;
    internal byte[] Registers { get; } = new byte[(int)Ym7128BRegister.Count];
    internal short[] Gains { get; } = new short[(int)Ym7128BRegister.T0];
    internal nuint[] Taps { get; } = new nuint[Ym7128BDatasheetSpecs.TapCount];
}

/*
typedef struct YM7128B_ChipShort_Process_Data
{
    YM7128B_Fixed inputs[YM7128B_InputChannel_Count];
    YM7128B_Fixed outputs[YM7128B_OutputChannel_Count];
} YM7128B_ChipShort_Process_Data;
*/
internal sealed class Ym7128BChipShortProcessData {
    internal short[] Inputs { get; } = new short[(int)Ym7128BInputChannel.Count];
    internal short[] Outputs { get; } = new short[(int)Ym7128BOutputChannel.Count];
}