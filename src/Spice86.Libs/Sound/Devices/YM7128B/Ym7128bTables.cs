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

namespace Spice86.Libs.Sound.Devices.YM7128B;

internal static class Ym7128BTables {
    private static readonly sbyte[] GainDecibelData = [
        -128,
        -60,
        -58,
        -56,
        -54,
        -52,
        -50,
        -48,
        -46,
        -44,
        -42,
        -40,
        -38,
        -36,
        -34,
        -32,
        -30,
        -28,
        -26,
        -24,
        -22,
        -20,
        -18,
        -16,
        -14,
        -12,
        -10,
        -8,
        -6,
        -4,
        -2,
        0
    ];

    private static readonly short[] GainFixedData = [
        -1,
        -33,
        -33,
        -49,
        -65,
        -81,
        -97,
        -129,
        -161,
        -193,
        -257,
        -321,
        -401,
        -513,
        -641,
        -817,
        -1025,
        -1297,
        -1633,
        -2065,
        -2593,
        -3265,
        -4113,
        -5185,
        -6529,
        -8225,
        -10353,
        -13041,
        -16417,
        -20673,
        -26017,
        -32753,
        0,
        32,
        32,
        48,
        64,
        80,
        96,
        128,
        160,
        192,
        256,
        320,
        400,
        512,
        640,
        816,
        1024,
        1296,
        1632,
        2064,
        2592,
        3264,
        4112,
        5184,
        6528,
        8224,
        10352,
        13040,
        16416,
        20672,
        26016,
        32752
    ];

    private static readonly float[] GainFloatData = [
        -0.000000000000000000f,
        -0.001000000000000000f,
        -0.001258925411794167f,
        -0.001584893192461114f,
        -0.001995262314968879f,
        -0.002511886431509579f,
        -0.003162277660168379f,
        -0.003981071705534973f,
        -0.005011872336272725f,
        -0.006309573444801930f,
        -0.007943282347242814f,
        -0.010000000000000000f,
        -0.012589254117941675f,
        -0.015848931924611134f,
        -0.019952623149688799f,
        -0.025118864315095794f,
        -0.031622776601683791f,
        -0.039810717055349734f,
        -0.050118723362727220f,
        -0.063095734448019331f,
        -0.079432823472428138f,
        -0.100000000000000006f,
        -0.125892541179416728f,
        -0.158489319246111343f,
        -0.199526231496887974f,
        -0.251188643150958013f,
        -0.316227766016837941f,
        -0.398107170553497203f,
        -0.501187233627272244f,
        -0.630957344480193250f,
        -0.794328234724281490f,
        -1.000000000000000000f,
        0.000000000000000000f,
        0.001000000000000000f,
        0.001258925411794167f,
        0.001584893192461114f,
        0.001995262314968879f,
        0.002511886431509579f,
        0.003162277660168379f,
        0.003981071705534973f,
        0.005011872336272725f,
        0.006309573444801930f,
        0.007943282347242814f,
        0.010000000000000000f,
        0.012589254117941675f,
        0.015848931924611134f,
        0.019952623149688799f,
        0.025118864315095794f,
        0.031622776601683791f,
        0.039810717055349734f,
        0.050118723362727220f,
        0.063095734448019331f,
        0.079432823472428138f,
        0.100000000000000006f,
        0.125892541179416728f,
        0.158489319246111343f,
        0.199526231496887974f,
        0.251188643150958013f,
        0.316227766016837941f,
        0.398107170553497203f,
        0.501187233627272244f,
        0.630957344480193250f,
        0.794328234724281490f,
        1.000000000000000000f
    ];

    private static readonly short[] GainShortData = [
        0,
        -32,
        -41,
        -51,
        -65,
        -82,
        -103,
        -130,
        -164,
        -206,
        -260,
        -327,
        -412,
        -519,
        -653,
        -823,
        -1036,
        -1304,
        -1642,
        -2067,
        -2602,
        -3276,
        -4125,
        -5193,
        -6537,
        -8230,
        -10361,
        -13044,
        -16422,
        -20674,
        -26027,
        -32767,
        0,
        32,
        41,
        51,
        65,
        82,
        103,
        130,
        164,
        206,
        260,
        327,
        412,
        519,
        653,
        823,
        1036,
        1304,
        1642,
        2067,
        2602,
        3276,
        4125,
        5193,
        6537,
        8230,
        10361,
        13044,
        16422,
        20674,
        26027,
        32767
    ];

    private static readonly ushort[] TapData = [
        0,
        75,
        151,
        227,
        303,
        379,
        455,
        531,
        607,
        683,
        759,
        835,
        911,
        987,
        1063,
        1139,
        1215,
        1291,
        1367,
        1443,
        1519,
        1595,
        1671,
        1747,
        1823,
        1899,
        1975,
        2051,
        2127,
        2203,
        2279,
        2355
    ];

    private static readonly short[] OversamplerFixedKernelData = [
        192,
        -128,
        -544,
        224,
        1264,
        -352,
        -2928,
        416,
        10224,
        15904,
        10224,
        416,
        -2928,
        -352,
        1264,
        224,
        -544,
        -128,
        192
    ];

    private static readonly float[] OversamplerFloatKernelData = [
        0.005969087803865891f,
        -0.003826518613910499f,
        -0.016623943725986925f,
        0.007053928712894589f,
        0.038895802111020034f,
        -0.010501507751597486f,
        -0.089238395139830201f,
        0.013171814880420758f,
        0.312314472963171053f,
        0.485820312497107776f,
        0.312314472963171053f,
        0.013171814880420758f,
        -0.089238395139830201f,
        -0.010501507751597486f,
        0.038895802111020034f,
        0.007053928712894589f,
        -0.016623943725986925f,
        -0.003826518613910499f,
        0.005969087803865891f
    ];

    // YM7128B_GainDecibel_Table
    internal static ReadOnlySpan<sbyte> GainDecibel => GainDecibelData;

    // YM7128B_GainFixed_Table
    internal static ReadOnlySpan<short> GainFixed => GainFixedData;

    // YM7128B_GainFloat_Table
    internal static ReadOnlySpan<float> GainFloat => GainFloatData;

    // YM7128B_GainShort_Table
    internal static ReadOnlySpan<short> GainShort => GainShortData;

    // YM7128B_Tap_Table
    internal static ReadOnlySpan<ushort> Tap => TapData;

    // YM7128B_OversamplerFixed_Kernel (linear phase)
    internal static ReadOnlySpan<short> OversamplerFixedKernel => OversamplerFixedKernelData;

    // YM7128B_OversamplerFloat_Kernel (linear phase)
    internal static ReadOnlySpan<float> OversamplerFloatKernel => OversamplerFloatKernelData;
}