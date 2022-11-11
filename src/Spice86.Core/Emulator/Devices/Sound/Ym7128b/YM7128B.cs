#define YM7128B_USE_MINPHASE //!< Enables minimum-phase oversampler kernel
namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;

public static class Ym7128B {
    public const int Ym7128BFloatMin = -1;
    public const int Ym7128BFloatMax = 1;

    public const string Version = "0.1.1";

    static readonly ReadOnlyCollection<sbyte> GainDecibelTable = Array.AsReadOnly(new sbyte[]
    {
    -128,  //  0 = -oo
    - 60,  //  1
    - 58,  //  2
    - 56,  //  3
    - 54,  //  4
    - 52,  //  5
    - 50,  //  6
    - 48,  //  7
    - 46,  //  8
    - 44,  //  9
    - 42,  // 10
    - 40,  // 11
    - 38,  // 12
    - 36,  // 13
    - 34,  // 14
    - 32,  // 15
    - 30,  // 16
    - 28,  // 17
    - 26,  // 18
    - 24,  // 19
    - 22,  // 20
    - 20,  // 21
    - 18,  // 22
    - 16,  // 23
    - 14,  // 24
    - 12,  // 25
    - 10,  // 26
    -  8,  // 27
    -  6,  // 28
    -  4,  // 29
    -  2,  // 30
       0   // 31
});

    private static int GainFixed(double real) => (short)(real * (int)ImplementationSpecs.GainMax) & unchecked((short)ImplementationSpecs.GainMask);

    static readonly ReadOnlyCollection<short> GainFixedTable = Array.AsReadOnly(new[]
{
    // Pseudo-negative gains
    (short)~GainFixed(0.000000000000000000),  // -oo dB-
    (short)~GainFixed(0.001000000000000000),  // -60 dB-
    (short)~GainFixed(0.001258925411794167),  // -58 dB-
    (short)~GainFixed(0.001584893192461114),  // -56 dB-
    (short)~GainFixed(0.001995262314968879),  // -54 dB-
    (short)~GainFixed(0.002511886431509579),  // -52 dB-
    (short)~GainFixed(0.003162277660168379),  // -50 dB-
    (short)~GainFixed(0.003981071705534973),  // -48 dB-
    (short)~GainFixed(0.005011872336272725),  // -46 dB-
    (short)~GainFixed(0.006309573444801930),  // -44 dB-
    (short)~GainFixed(0.007943282347242814),  // -42 dB-
    (short)~GainFixed(0.010000000000000000),  // -40 dB-
    (short)~GainFixed(0.012589254117941675),  // -38 dB-
    (short)~GainFixed(0.015848931924611134),  // -36 dB-
    (short)~GainFixed(0.019952623149688799),  // -34 dB-
    (short)~GainFixed(0.025118864315095794),  // -32 dB-
    (short)~GainFixed(0.031622776601683791),  // -30 dB-
    (short)~GainFixed(0.039810717055349734),  // -28 dB-
    (short)~GainFixed(0.050118723362727220),  // -26 dB-
    (short)~GainFixed(0.063095734448019331),  // -24 dB-
    (short)~GainFixed(0.079432823472428138),  // -22 dB-
    (short)~GainFixed(0.100000000000000006),  // -20 dB-
    (short)~GainFixed(0.125892541179416728),  // -18 dB-
    (short)~GainFixed(0.158489319246111343),  // -16 dB-
    (short)~GainFixed(0.199526231496887974),  // -14 dB-
    (short)~GainFixed(0.251188643150958013),  // -12 dB-
    (short)~GainFixed(0.316227766016837941),  // -10 dB-
    (short)~GainFixed(0.398107170553497203),  // - 8 dB-
    (short)~GainFixed(0.501187233627272244),  // - 6 dB-
    (short)~GainFixed(0.630957344480193250),  // - 4 dB-
    (short)~GainFixed(0.794328234724281490),  // - 2 dB-
    (short)~GainFixed(1.000000000000000000),  // - 0 dB-

    // Positive gains
    (short)+GainFixed(0.000000000000000000),  // -oo dB(short)+
    (short)+GainFixed(0.001000000000000000),  // -60 dB(short)+
    (short)+GainFixed(0.001258925411794167),  // -58 dB(short)+
    (short)+GainFixed(0.001584893192461114),  // -56 dB(short)+
    (short)+GainFixed(0.001995262314968879),  // -54 dB(short)+
    (short)+GainFixed(0.002511886431509579),  // -52 dB(short)+
    (short)+GainFixed(0.003162277660168379),  // -50 dB(short)+
    (short)+GainFixed(0.003981071705534973),  // -48 dB(short)+
    (short)+GainFixed(0.005011872336272725),  // -46 dB(short)+
    (short)+GainFixed(0.006309573444801930),  // -44 dB(short)+
    (short)+GainFixed(0.007943282347242814),  // -42 dB(short)+
    (short)+GainFixed(0.010000000000000000),  // -40 dB(short)+
    (short)+GainFixed(0.012589254117941675),  // -38 dB(short)+
    (short)+GainFixed(0.015848931924611134),  // -36 dB(short)+
    (short)+GainFixed(0.019952623149688799),  // -34 dB(short)+
    (short)+GainFixed(0.025118864315095794),  // -32 dB(short)+
    (short)+GainFixed(0.031622776601683791),  // -30 dB(short)+
    (short)+GainFixed(0.039810717055349734),  // -28 dB(short)+
    (short)+GainFixed(0.050118723362727220),  // -26 dB(short)+
    (short)+GainFixed(0.063095734448019331),  // -24 dB(short)+
    (short)+GainFixed(0.079432823472428138),  // -22 dB(short)+
    (short)+GainFixed(0.100000000000000006),  // -20 dB(short)+
    (short)+GainFixed(0.125892541179416728),  // -18 dB(short)+
    (short)+GainFixed(0.158489319246111343),  // -16 dB(short)+
    (short)+GainFixed(0.199526231496887974),  // -14 dB(short)+
    (short)+GainFixed(0.251188643150958013),  // -12 dB(short)+
    (short)+GainFixed(0.316227766016837941),  // -10 dB(short)+
    (short)+GainFixed(0.398107170553497203),  // - 8 dB(short)+
    (short)+GainFixed(0.501187233627272244),  // - 6 dB(short)+
    (short)+GainFixed(0.630957344480193250),  // - 4 dB(short)+
    (short)+GainFixed(0.794328234724281490),  // - 2 dB(short)+
    (short)+GainFixed(1.000000000000000000)   // - 0 dB+
});
    static readonly ReadOnlyCollection<double> GainFloatTable = Array.AsReadOnly(new[]
{
    // Negative gains
    -0.000000000000000000,  // -oo dB-
    -0.001000000000000000,  // -60 dB-
    -0.001258925411794167,  // -58 dB-
    -0.001584893192461114,  // -56 dB-
    -0.001995262314968879,  // -54 dB-
    -0.002511886431509579,  // -52 dB-
    -0.003162277660168379,  // -50 dB-
    -0.003981071705534973,  // -48 dB-
    -0.005011872336272725,  // -46 dB-
    -0.006309573444801930,  // -44 dB-
    -0.007943282347242814,  // -42 dB-
    -0.010000000000000000,  // -40 dB-
    -0.012589254117941675,  // -38 dB-
    -0.015848931924611134,  // -36 dB-
    -0.019952623149688799,  // -34 dB-
    -0.025118864315095794,  // -32 dB-
    -0.031622776601683791,  // -30 dB-
    -0.039810717055349734,  // -28 dB-
    -0.050118723362727220,  // -26 dB-
    -0.063095734448019331,  // -24 dB-
    -0.079432823472428138,  // -22 dB-
    -0.100000000000000006,  // -20 dB-
    -0.125892541179416728,  // -18 dB-
    -0.158489319246111343,  // -16 dB-
    -0.199526231496887974,  // -14 dB-
    -0.251188643150958013,  // -12 dB-
    -0.316227766016837941,  // -10 dB-
    -0.398107170553497203,  // - 8 dB-
    -0.501187233627272244,  // - 6 dB-
    -0.630957344480193250,  // - 4 dB-
    -0.794328234724281490,  // - 2 dB-
    -1.000000000000000000,  // - 0 dB-

    // Positive gains
    +0.000000000000000000,  // -oo dB+
    +0.001000000000000000,  // -60 dB+
    +0.001258925411794167,  // -58 dB+
    +0.001584893192461114,  // -56 dB+
    +0.001995262314968879,  // -54 dB+
    +0.002511886431509579,  // -52 dB+
    +0.003162277660168379,  // -50 dB+
    +0.003981071705534973,  // -48 dB+
    +0.005011872336272725,  // -46 dB+
    +0.006309573444801930,  // -44 dB+
    +0.007943282347242814,  // -42 dB+
    +0.010000000000000000,  // -40 dB+
    +0.012589254117941675,  // -38 dB+
    +0.015848931924611134,  // -36 dB+
    +0.019952623149688799,  // -34 dB+
    +0.025118864315095794,  // -32 dB+
    +0.031622776601683791,  // -30 dB+
    +0.039810717055349734,  // -28 dB+
    +0.050118723362727220,  // -26 dB+
    +0.063095734448019331,  // -24 dB+
    +0.079432823472428138,  // -22 dB+
    +0.100000000000000006,  // -20 dB+
    +0.125892541179416728,  // -18 dB+
    +0.158489319246111343,  // -16 dB+
    +0.199526231496887974,  // -14 dB+
    +0.251188643150958013,  // -12 dB+
    +0.316227766016837941,  // -10 dB+
    +0.398107170553497203,  // - 8 dB+
    +0.501187233627272244,  // - 6 dB+
    +0.630957344480193250,  // - 4 dB+
    +0.794328234724281490,  // - 2 dB+
    +1.000000000000000000   // - 0 dB+
});
    static readonly ReadOnlyCollection<double> YM7128B_GainFloat_Table = Array.AsReadOnly(new double[]
{
    // Negative gains
    -0.000000000000000000,  // -oo dB-
    -0.001000000000000000,  // -60 dB-
    -0.001258925411794167,  // -58 dB-
    -0.001584893192461114,  // -56 dB-
    -0.001995262314968879,  // -54 dB-
    -0.002511886431509579,  // -52 dB-
    -0.003162277660168379,  // -50 dB-
    -0.003981071705534973,  // -48 dB-
    -0.005011872336272725,  // -46 dB-
    -0.006309573444801930,  // -44 dB-
    -0.007943282347242814,  // -42 dB-
    -0.010000000000000000,  // -40 dB-
    -0.012589254117941675,  // -38 dB-
    -0.015848931924611134,  // -36 dB-
    -0.019952623149688799,  // -34 dB-
    -0.025118864315095794,  // -32 dB-
    -0.031622776601683791,  // -30 dB-
    -0.039810717055349734,  // -28 dB-
    -0.050118723362727220,  // -26 dB-
    -0.063095734448019331,  // -24 dB-
    -0.079432823472428138,  // -22 dB-
    -0.100000000000000006,  // -20 dB-
    -0.125892541179416728,  // -18 dB-
    -0.158489319246111343,  // -16 dB-
    -0.199526231496887974,  // -14 dB-
    -0.251188643150958013,  // -12 dB-
    -0.316227766016837941,  // -10 dB-
    -0.398107170553497203,  // - 8 dB-
    -0.501187233627272244,  // - 6 dB-
    -0.630957344480193250,  // - 4 dB-
    -0.794328234724281490,  // - 2 dB-
    -1.000000000000000000,  // - 0 dB-

    // Positive gains
    +0.000000000000000000,  // -oo dB+
    +0.001000000000000000,  // -60 dB+
    +0.001258925411794167,  // -58 dB+
    +0.001584893192461114,  // -56 dB+
    +0.001995262314968879,  // -54 dB+
    +0.002511886431509579,  // -52 dB+
    +0.003162277660168379,  // -50 dB+
    +0.003981071705534973,  // -48 dB+
    +0.005011872336272725,  // -46 dB+
    +0.006309573444801930,  // -44 dB+
    +0.007943282347242814,  // -42 dB+
    +0.010000000000000000,  // -40 dB+
    +0.012589254117941675,  // -38 dB+
    +0.015848931924611134,  // -36 dB+
    +0.019952623149688799,  // -34 dB+
    +0.025118864315095794,  // -32 dB+
    +0.031622776601683791,  // -30 dB+
    +0.039810717055349734,  // -28 dB+
    +0.050118723362727220,  // -26 dB+
    +0.063095734448019331,  // -24 dB+
    +0.079432823472428138,  // -22 dB+
    +0.100000000000000006,  // -20 dB+
    +0.125892541179416728,  // -18 dB+
    +0.158489319246111343,  // -16 dB+
    +0.199526231496887974,  // -14 dB+
    +0.251188643150958013,  // -12 dB+
    +0.316227766016837941,  // -10 dB+
    +0.398107170553497203,  // - 8 dB+
    +0.501187233627272244,  // - 6 dB+
    +0.630957344480193250,  // - 4 dB+
    +0.794328234724281490,  // - 2 dB+
    +1.000000000000000000   // - 0 dB+
});

    private static short Tap(int index) => (short)(index * ((int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length - 1) / ((int)YM7128B_DatasheetSpecs.YM7128B_Tap_Value_Count - 1));

    private static short GainShort(double real) => (short)(real * (int)ImplementationSpecs.GainMax);

    static readonly ReadOnlyCollection<short> GainShortTable = Array.AsReadOnly(new[]
    {
        // Negative gains
        (short)-GainShort(0.000000000000000000),  // -oo dB-
        (short)-GainShort(0.001000000000000000),  // -60 dB-
        (short)-GainShort(0.001258925411794167),  // -58 dB-
        (short)-GainShort(0.001584893192461114),  // -56 dB-
        (short)-GainShort(0.001995262314968879),  // -54 dB-
        (short)-GainShort(0.002511886431509579),  // -52 dB-
        (short)-GainShort(0.003162277660168379),  // -50 dB-
        (short)-GainShort(0.003981071705534973),  // -48 dB-
        (short)-GainShort(0.005011872336272725),  // -46 dB-
        (short)-GainShort(0.006309573444801930),  // -44 dB-
        (short)-GainShort(0.007943282347242814),  // -42 dB-
        (short)-GainShort(0.010000000000000000),  // -40 dB-
        (short)-GainShort(0.012589254117941675),  // -38 dB-
        (short)-GainShort(0.015848931924611134),  // -36 dB-
        (short)-GainShort(0.019952623149688799),  // -34 dB-
        (short)-GainShort(0.025118864315095794),  // -32 dB-
        (short)-GainShort(0.031622776601683791),  // -30 dB-
        (short)-GainShort(0.039810717055349734),  // -28 dB-
        (short)-GainShort(0.050118723362727220),  // -26 dB-
        (short)-GainShort(0.063095734448019331),  // -24 dB-
        (short)-GainShort(0.079432823472428138),  // -22 dB-
        (short)-GainShort(0.100000000000000006),  // -20 dB-
        (short)-GainShort(0.125892541179416728),  // -18 dB-
        (short)-GainShort(0.158489319246111343),  // -16 dB-
        (short)-GainShort(0.199526231496887974),  // -14 dB-
        (short)-GainShort(0.251188643150958013),  // -12 dB-
        (short)-GainShort(0.316227766016837941),  // -10 dB-
        (short)-GainShort(0.398107170553497203),  // - 8 dB-
        (short)-GainShort(0.501187233627272244),  // - 6 dB-
        (short)-GainShort(0.630957344480193250),  // - 4 dB-
        (short)-GainShort(0.794328234724281490),  // - 2 dB-
        (short)-GainShort(1.000000000000000000),  // - 0 dB-

        // Positive gains
        (short)+GainShort(0.000000000000000000),  // -oo dB(short)+
        (short)+GainShort(0.001000000000000000),  // -60 dB(short)+
        (short)+GainShort(0.001258925411794167),  // -58 dB(short)+
        (short)+GainShort(0.001584893192461114),  // -56 dB(short)+
        (short)+GainShort(0.001995262314968879),  // -54 dB(short)+
        (short)+GainShort(0.002511886431509579),  // -52 dB(short)+
        (short)+GainShort(0.003162277660168379),  // -50 dB(short)+
        (short)+GainShort(0.003981071705534973),  // -48 dB(short)+
        (short)+GainShort(0.005011872336272725),  // -46 dB(short)+
        (short)+GainShort(0.006309573444801930),  // -44 dB(short)+
        (short)+GainShort(0.007943282347242814),  // -42 dB(short)+
        (short)+GainShort(0.010000000000000000),  // -40 dB(short)+
        (short)+GainShort(0.012589254117941675),  // -38 dB(short)+
        (short)+GainShort(0.015848931924611134),  // -36 dB(short)+
        (short)+GainShort(0.019952623149688799),  // -34 dB(short)+
        (short)+GainShort(0.025118864315095794),  // -32 dB(short)+
        (short)+GainShort(0.031622776601683791),  // -30 dB(short)+
        (short)+GainShort(0.039810717055349734),  // -28 dB(short)+
        (short)+GainShort(0.050118723362727220),  // -26 dB(short)+
        (short)+GainShort(0.063095734448019331),  // -24 dB(short)+
        (short)+GainShort(0.079432823472428138),  // -22 dB(short)+
        (short)+GainShort(0.100000000000000006),  // -20 dB(short)+
        (short)+GainShort(0.125892541179416728),  // -18 dB(short)+
        (short)+GainShort(0.158489319246111343),  // -16 dB(short)+
        (short)+GainShort(0.199526231496887974),  // -14 dB(short)+
        (short)+GainShort(0.251188643150958013),  // -12 dB(short)+
        (short)+GainShort(0.316227766016837941),  // -10 dB(short)+
        (short)+GainShort(0.398107170553497203),  // - 8 dB(short)+
        (short)+GainShort(0.501187233627272244),  // - 6 dB(short)+
        (short)+GainShort(0.630957344480193250),  // - 4 dB(short)+
        (short)+GainShort(0.794328234724281490),  // - 2 dB(short)+
        (short)+GainShort(1.000000000000000000)   // - 0 dB(short)+
    });

    private static ushort Tap(int index) => (ushort)(index * ((int)DatasheetSpecs.BufferLength - 1) / ((int)DatasheetSpecs.TapValueCount - 1));

    static readonly ReadOnlyCollection<ushort> TapTable = Array.AsReadOnly(new[]
{
    Tap( 0),  //   0.0 ms
    Tap( 1),  //   3.2 ms
    Tap( 2),  //   6.5 ms
    Tap( 3),  //   9.7 ms
    Tap( 4),  //  12.9 ms
    Tap( 5),  //  16.1 ms
    Tap( 6),  //  19.3 ms
    Tap( 7),  //  22.6 ms
    Tap( 8),  //  25.8 ms
    Tap( 9),  //  29.0 ms
    Tap(10),  //  32.3 ms
    Tap(11),  //  35.5 ms
    Tap(12),  //  38.7 ms
    Tap(13),  //  41.9 ms
    Tap(14),  //  45.2 ms
    Tap(15),  //  48.4 ms
    Tap(16),  //  51.6 ms
    Tap(17),  //  54.9 ms
    Tap(18),  //  58.1 ms
    Tap(19),  //  61.3 ms
    Tap(20),  //  64.5 ms
    Tap(21),  //  67.8 ms
    Tap(22),  //  71.0 ms
    Tap(23),  //  74.2 ms
    Tap(24),  //  77.4 ms
    Tap(25),  //  80.7 ms
    Tap(26),  //  83.9 ms
    Tap(27),  //  87.1 ms
    Tap(28),  //  90.4 ms
    Tap(29),  //  93.6 ms
    Tap(30),  //  96.8 ms
    Tap(31)   // 100.0 ms
});

    private static short Kernel(double real) {
        unchecked {
            return ((short)(((short)real) * ((short)ImplementationSpecs.FixedMax) & ((short)ImplementationSpecs.CoeffMask)));
        }
    }

    static readonly ReadOnlyCollection<short> OversamplerFixedKernelTable = Array.AsReadOnly(new[]
{
#if YM7128B_USE_MINPHASE
    // minimum phase
    Kernel(+0.073585247514714749),
    Kernel(+0.269340051166713890),
    Kernel(+0.442535202999738531),
    Kernel(+0.350129745841520346),
    Kernel(+0.026195691646307945),
    Kernel(-0.178423532471468610),
    Kernel(-0.081176763571493171),
    Kernel(+0.083194010466739091),
    Kernel(+0.067960765530891545),
    Kernel(-0.035840063980478287),
    Kernel(-0.044393769145659796),
    Kernel(+0.013156688603347873),
    Kernel(+0.023451305043275420),
    Kernel(-0.004374029821991059),
    Kernel(-0.009480786001493536),
    Kernel(+0.002700502551912207),
    Kernel(+0.003347671274177581),
    Kernel(-0.002391896275498628),
    Kernel(+0.000483958628744376)
#else
    // linear phase
    Kernel(+0.005969087803865891),
    Kernel(-0.003826518613910499),
    Kernel(-0.016623943725986926),
    Kernel(+0.007053928712894589),
    Kernel(+0.038895802111020034),
    Kernel(-0.010501507751597486),
    Kernel(-0.089238395139830201),
    Kernel(+0.013171814880420758),
    Kernel(+0.312314472963171053),
    Kernel(+0.485820312497107776),
    Kernel(+0.312314472963171053),
    Kernel(+0.013171814880420758),
    Kernel(-0.089238395139830201),
    Kernel(-0.010501507751597486),
    Kernel(+0.038895802111020034),
    Kernel(+0.007053928712894589),
    Kernel(-0.016623943725986926),
    Kernel(-0.003826518613910499),
    Kernel(+0.005969087803865891)
#endif
});

    public static short OversamplerFixedProcess(
        ref OversamplerFixed self,
        short input) {
        int accum = 0;
        for (byte i = 0; i < (byte)OversamplerSpecs.Length; ++i) {
            short sample = self.Buffer[i];
            self.Buffer[i] = input;
            input = sample;
            short kernel = OversamplerFixedKernelTable[i];
            short oversampled = MulFixed(sample, kernel);
            accum += oversampled;
        }
        short clamped = ClampFixed(accum);
        unchecked {
            short output = (short)(clamped & (short)ImplementationSpecs.SignalMask);
            return output;
        }
    }

    private static double KernelDouble(double real) => real;

    static readonly ReadOnlyCollection<double> OversamplerFloatKernelTable = Array.AsReadOnly(new[]
{
#if YM7128B_USE_MINPHASE
    // minimum phase
    KernelDouble(+0.073585247514714749),
    KernelDouble(+0.269340051166713890),
    KernelDouble(+0.442535202999738531),
    KernelDouble(+0.350129745841520346),
    KernelDouble(+0.026195691646307945),
    KernelDouble(-0.178423532471468610),
    KernelDouble(-0.081176763571493171),
    KernelDouble(+0.083194010466739091),
    KernelDouble(+0.067960765530891545),
    KernelDouble(-0.035840063980478287),
    KernelDouble(-0.044393769145659796),
    KernelDouble(+0.013156688603347873),
    KernelDouble(+0.023451305043275420),
    KernelDouble(-0.004374029821991059),
    KernelDouble(-0.009480786001493536),
    KernelDouble(+0.002700502551912207),
    KernelDouble(+0.003347671274177581),
    KernelDouble(-0.002391896275498628),
    KernelDouble(+0.000483958628744376)
#else
    // linear phase
    KernelDouble(+0.005969087803865891),
    KernelDouble(-0.003826518613910499),
    KernelDouble(-0.016623943725986926),
    KernelDouble(+0.007053928712894589),
    KernelDouble(+0.038895802111020034),
    KernelDouble(-0.010501507751597486),
    KernelDouble(-0.089238395139830201),
    KernelDouble(+0.013171814880420758),
    KernelDouble(+0.312314472963171053),
    KernelDouble(+0.485820312497107776),
    KernelDouble(+0.312314472963171053),
    KernelDouble(+0.013171814880420758),
    KernelDouble(-0.089238395139830201),
    KernelDouble(-0.010501507751597486),
    KernelDouble(+0.038895802111020034),
    KernelDouble(+0.007053928712894589),
    KernelDouble(-0.016623943725986926),
    KernelDouble(-0.003826518613910499),
    KernelDouble(+0.005969087803865891)
#endif
});

    public static double OversamplerFloatProcess(
        ref OversamplerFloat self,
        double input) {
        double accum = 0;

        for (byte i = 0; i < (byte)OversamplerSpecs.Length; ++i) {
            (input, self.Buffer[i]) = (self.Buffer[i], input);
            double kernel = OversamplerFloatKernelTable[i];
            double oversampled = MulFloat(self.Buffer[i], kernel);
            accum += oversampled;
        }

        double output = ClampFloat(accum);
        return output;
    }

    public static void ChipFixedReset(ref ChipFixed self) {
        for (byte i = 0; i <= (byte)DatasheetSpecs.AddressMax; ++i) {
            self.Regs[i] = 0;
        }
    }

    public static void ChipFixedStart(ref ChipFixed self) {
        self.T0d = 0;

        self.Tail = 0;

        for (ushort i = 0; i < (int)DatasheetSpecs.BufferLength; ++i) {
            self.Buffer[i] = 0;
        }

        for (byte i = 0; i < (int)OutputChannel.Count; ++i) {
            OversamplerFixedReset(ref self.Oversampler[i]);
        }
    }

    public static void OversamplerFixedReset(ref OversamplerFixed self) {
        OversamplerFixedClear(ref self, 0);
    }

    public static void OversamplerFixedClear(
        ref OversamplerFixed self,
        short input
    ) {
        for (byte index = 0; index < (int)OversamplerSpecs.Length; ++index) {
            self.Buffer[index] = input;
        }
    }

    private static double MulFloat(double a, double b) => a * b;

    private static double AddFloat(double a, double b) => a + b;

    private static double ClampFloat(double signal) {
        if (signal < Ym7128BFloatMin) {
            return Ym7128BFloatMin;
        }
        if (signal > Ym7128BFloatMax) {
            return Ym7128BFloatMax;
        }
        return signal;
    }

    private static short MulFixed(short a, short b) {
        unchecked {
            int aa = a & (short)ImplementationSpecs.OperandMask;
            int bb = b & (short)ImplementationSpecs.OperandMask;
            int mm = aa * bb;
            short x = (short)(mm >> (short)ImplementationSpecs.FixedDecimals);
            short y = (short)(x & (short)ImplementationSpecs.OperandMask);
            return y;
        }
    }

    private static short ClampFixed(int signal) {
        if (signal < (int)ImplementationSpecs.FixedMin) {
            signal = (int)ImplementationSpecs.FixedMin;
        }
        if (signal > (int)ImplementationSpecs.FixedMax) {
            signal = (int)ImplementationSpecs.FixedMax;
        }
        unchecked {
            return (short)((short)signal & (short)ImplementationSpecs.OperandMask);
        }
    }

    private static short ClampAddFixed(short a, short b) {
        int aa = a & (int)ImplementationSpecs.OperandMask;
        int bb = b & (int)ImplementationSpecs.OperandMask;
        int ss = aa + bb;
        short y = ClampFixed(ss);
        return y;
    }

    public static unsafe void ChipFixedProcess(
        ref ChipFixed self,
        ref ChipFixedProcessData data
    ) {
        short input = data.Inputs[(int)InputChannel.Mono];
        short sample = (short)(input & (int)ImplementationSpecs.SignalMask);

        ushort t0 = (ushort)(self.Tail + self.Taps[0]);
        ushort filterHead = (ushort)((t0 >= (int)DatasheetSpecs.BufferLength) ? (t0 - (int)DatasheetSpecs.BufferLength) : t0);
        short filterT0 = self.Buffer[filterHead];
        short filterD = self.T0d;
        self.T0d = filterT0;
        short filterC0 = MulFixed(filterT0, self.Gains[(int)Reg.C0]);
        short filterC1 = MulFixed(filterD, self.Gains[(int)Reg.C1]);
        short filterSum = ClampAddFixed(filterC0, filterC1);
        short filterVc = MulFixed(filterSum, self.Gains[(int)Reg.Vc]);

        short inputVm = MulFixed(sample, self.Gains[(int)Reg.Vm]);
        short inputSum = ClampAddFixed(inputVm, filterVc);

        self.Tail = self.Tail == 1 ? (short)(self.Tail - 1) : (short)(DatasheetSpecs.BufferLength - 1);
        self.Buffer[self.Tail] = inputSum;

        for (byte channel = 0; channel < (int)OutputChannel.Count; ++channel) {
            byte gb = (byte)((int)Reg.Gl1 + (channel * (int)DatasheetSpecs.GainLaneCount));
            int accum = 0;

            for (byte tap = 1; tap < (int)DatasheetSpecs.TapCount; ++tap) {
                ushort t = (ushort)(self.Tail + self.Taps[tap]);
                ushort head = (ushort)((t >= (int)DatasheetSpecs.BufferLength) ? (t - (int)DatasheetSpecs.BufferLength) : t);
                short buffered = self.Buffer[head];
                short g = self.Gains[gb + tap - 1];
                short bufferedG = MulFixed(buffered, g);
                accum += bufferedG;
            }

            short total = ClampFixed(accum);
            short v = self.Gains[(int)Reg.Vl + channel];
            short totalV = MulFixed(total, v);

            OversamplerFixed oversampler = self.Oversampler[channel];
            short[] outputs = data.Outputs[channel];

            outputs[0] = OversamplerFixedProcess(ref oversampler, totalV);
            for (byte j = 1; j < (int)DatasheetSpecs.Oversampling; ++j) {
                outputs[j] = (byte)OversamplerFixedProcess(ref oversampler, 0);
            }
        }
    }

    [Pure]
    public static byte ChipFloatRead(
        ref ChipFloat self,
        byte address
    ) {
        if (address < (int)Reg.C0) {
            return (byte)(self.Regs[address] & (int)DatasheetSpecs.GainDataMask);
        } else if (address < (int)Reg.T0) {
            return (byte)(self.Regs[address] & (int)DatasheetSpecs.CoeffValueMask);
        } else if (address < (int)Reg.Count) {
            return (byte)(self.Regs[address] & (int)DatasheetSpecs.TapValueMask);
        }
        return 0;
    }

    public static ushort RegisterToTap(byte data) {
        byte i = (byte)(data & (int)DatasheetSpecs.TapValueMask);
        ushort t = TapTable[i];
        return t;
    }

    public static ushort RegisterToTapIdeal(
        byte data,
        uint sampleRate
    ) {
        byte i = (byte)(data & (int)DatasheetSpecs.TapValueMask);
        ushort t = (ushort)((i * (sampleRate / 10)) / ((int)DatasheetSpecs.TapValueCount - 1));
        return t;
    }

    private static short RegisterToGainFixed(byte data) {
        byte i = (byte)(data & (int)DatasheetSpecs.GainDataMask);
        short g = GainFixedTable[i];
        return g;
    }

    private static double RegisterToGainFloat(byte data) {
        byte i = (byte)(data & (int)DatasheetSpecs.GainDataMask);
        double g = GainFloatTable[i];
        return g;
    }

    private static short RegisterToGainShort(byte data) {
        byte i = (byte)(data & (int)DatasheetSpecs.GainDataMask);
        short g = GainShortTable[i];
        return g;
    }


    private static short RegisterToCoeffFixed(byte data) {
        byte r = (byte)(data & (int)DatasheetSpecs.CoeffValueMask);
        const byte sh = (byte)(ImplementationSpecs.FixedBits - (int)DatasheetSpecs.CoeffValueBits);
        short c = (short)(r << sh);
        return c;
    }

    private static double RegisterToCoeffFloat(byte data) {
        short k = RegisterToCoeffFixed(data);
        double c = k * (1 / (double)ImplementationSpecs.GainMax);
        return c;
    }

    private static short RegisterToCoeffShort(byte data) {
        byte r = (byte)(data & (int)DatasheetSpecs.CoeffValueMask);
        byte sh = (int)ImplementationSpecs.FixedBits - (int)DatasheetSpecs.CoeffValueBits;
        short c = (short)(r << sh);
        return c;
    }

    public static void ChipWrite(
        ref ChipFloat self,
        byte address,
        byte data
    ) {
        if (address < (int)Reg.C0) {
            self.Regs[address] = (byte)(data & (int)DatasheetSpecs.GainDataMask);
            self.Gains[address] = RegisterToGainFloat(data);
        } else if (address < (int)Reg.T0) {
            self.Regs[address] = (byte)(data & (int)DatasheetSpecs.CoeffValueMask);
            self.Gains[address] = RegisterToCoeffFloat(data);
        } else if (address < (int)Reg.Count) {
            self.Regs[address] = (byte)(data & (int)DatasheetSpecs.TapValueMask);
            self.Taps[address - (int)Reg.T0] = RegisterToTap(data);
        }
    }

    public static void ChipIdealReset(ref ChipIdeal self) {
        for (int i = 0; i < (int)Reg.Count; ++i)
            self.Regs[i] = 0;

        for (int i = 0; i < (int)Reg.T0; ++i)
            self.Gains[i] = 0.0f;

        // self.Taps[] are populated by ChipIdealSetup()
        self.T0d = 0.0f;
        self.Tail = 0;

        // Only zero the buffer if we have one
        if (self.Buffer.Length >= 1 && self.Length >= 1) {
            self.Buffer = new double[self.Length];
        }
        // if we have a buffer, then sample rate must be set
        if (self.SampleRate <= 0) {
            throw new InvalidOperationException("Sample rate must be set");
        }
    }

    public static void ChipIdealStart(ref ChipIdeal self) {
        self.T0d = 0;

        self.Tail = 0;

        if (self.Buffer.Length > 0) {
            for (int i = 0; i < self.Length; ++i) {
                self.Buffer[i] = 0;
            }
        }
    }

    public static void ChipIdealProcess(
        ref ChipIdeal self,
        ref ChipIdealProcessData data) {
        if (self.Buffer.Length == 0) {
            return;
        }

        double input = data.Inputs[(int)InputChannel.Mono];
        double sample = input;

        int t0 = self.Tail + self.Taps[0];
        int filterHead = (t0 >= self.Length) ? (t0 - self.Length) : t0;
        double filterT0 = self.Buffer[filterHead];
        double filterD = self.T0d;
        self.T0d = filterT0;
        double filterC0 = MulFloat(filterT0, self.Gains[(int)Reg.C0]);
        double filterC1 = MulFloat(filterD, self.Gains[(int)Reg.C1]);
        double filterSum = AddFloat(filterC0, filterC1);
        double filterVc = MulFloat(filterSum, self.Gains[(int)Reg.Vc]);

        double inputVm = MulFloat(sample, self.Gains[(int)Reg.Vm]);
        double inputSum = AddFloat(inputVm, filterVc);

        self.Tail = (ushort)(self.Tail >= 1 ? (self.Tail - 1) : (self.Length - 1));
        self.Buffer[self.Tail] = inputSum;

        for (byte channel = 0; channel < (byte)OutputChannel.Count; ++channel) {
            byte gb = (byte)((byte)Reg.Gl1 + (channel * (byte)DatasheetSpecs.GainLaneCount));
            double accum = 0;

            for (byte tap = 1; tap < (byte)DatasheetSpecs.TapCount; ++tap) {
                int t = self.Tail + self.Taps[tap];
                int head = (t >= self.Length) ? (t - self.Length) : t;
                double buffered = self.Buffer[head];
                double g = self.Gains[gb + tap - 1];
                double bufferedG = MulFloat(buffered, g);
                accum += bufferedG;
            }

            double total = accum;
            double v = self.Gains[(int)Reg.Vl + channel];
            double totalV = MulFloat(total, v);
            const double og = 1 / (double)DatasheetSpecs.Oversampling;
            double oversampled = MulFloat(totalV, og);
            data.Outputs[channel] = (float)oversampled;
        }
    }

    public static byte ChipIdealRead(
        ref ChipIdeal self,
        byte address) {
        if (address < (byte)Reg.C0) {
            return (byte)(self.Regs[address] & (byte)DatasheetSpecs.GainDataMask);
        } else if (address < (byte)Reg.T0) {
            return (byte)(self.Regs[address] & (byte)DatasheetSpecs.CoeffValueMask);
        } else if (address < (byte)Reg.Count) {
            return (byte)(self.Regs[address] & (byte)DatasheetSpecs.TapValueMask);
        }
        return 0;
    }

    public static void ChipIdealWrite(
        ref ChipIdeal self,
        byte address,
        byte data) {
        if (address < (byte)Reg.C0) {
            self.Regs[address] = (byte)(data & (byte)DatasheetSpecs.GainDataMask);
            self.Gains[address] = RegisterToGainFloat(data);
        } else if (address < (byte)Reg.T0) {
            self.Regs[address] = (byte)(data & (byte)DatasheetSpecs.CoeffValueMask);
            self.Gains[address] = RegisterToCoeffFloat(data);
        } else if (address < (byte)Reg.Count) {
            self.Regs[address] = (byte)(data & (byte)DatasheetSpecs.TapValueMask);
            self.Taps[address - (byte)Reg.T0] = RegisterToTapIdeal(data, self.SampleRate);
        }
    }

    public static void ChipIdealSetup(
        ref ChipIdeal self,
        ushort sampleRate) {
        if (self.SampleRate != sampleRate) {
            self.SampleRate = sampleRate;

            if (self.Buffer.Length > 0) {
                self.Buffer = Array.Empty<double>();
            }

            if (sampleRate >= 10) {
                int length = (sampleRate / 10) + 1;
                self.Buffer = new double[length];

                for (byte i = 0; i < (byte)DatasheetSpecs.TapCount; ++i) {
                    byte data = self.Regs[i + (byte)Reg.T0];
                    self.Taps[i] = RegisterToTapIdeal(data, self.SampleRate);
                }
            } else {
                self.Buffer = Array.Empty<double>();
            }
        }
    }

    public static void ChipShortReset(ref ChipShort self) {
        for (byte i = 0; i <= (byte)DatasheetSpecs.AddressMax; ++i) {
            self.Regs[i] = 0;
        }
    }

    public static void ChipShortStart(ref ChipShort self) {
        self.T0d = 0;

        self.Tail = 0;

        if (self.Buffer.Length > 0) {
            for (byte i = 0; i < self.Length; ++i) {
                self.Buffer[i] = 0;
            }
        }
    }

    private static short MulShort(short a, short b) => (short)(a * b);

    private static short ClampAddShort(short a, short b) {
        int aa = a;
        int bb = b;
        int ss = aa + bb;
        short y = ClampShort(ss);
        return y;
    }

    private static short ClampShort(int signal) {
        if (signal < (int)ImplementationSpecs.FixedMin) {
            return short.MinValue + 1;
        } else if (signal > short.MaxValue) {
            return short.MaxValue;
        }
        return (short)signal;
    }

    public static void ChipShortProcess(
        ref ChipShort self,
        ChipShortProcessData data) {
        short input = data.Inputs[(int)InputChannel.Mono];
        short sample = input;

        ushort t0 = (ushort)(self.Tail + self.Taps[0]);
        ushort filterHead = (ushort)(t0 >= self.Length ? (t0 - self.Length) : t0);
        short filterT0 = self.Buffer[filterHead];
        short filterD = self.T0d;
        self.T0d = filterT0;
        short filterC0 = MulShort(filterT0, self.Gains[(int)Reg.C0]);
        short filterC1 = MulShort(filterD, self.Gains[(int)Reg.C1]);
        short filterSum = ClampAddShort(filterC0, filterC1);
        short filterVc = MulShort(filterSum, self.Gains[(int)Reg.Vc]);

        short inputVm = MulShort(sample, self.Gains[(int)Reg.Vm]);
        short inputSum = ClampAddShort(inputVm, filterVc);

        self.Tail = (ushort)(self.Tail > 0 ? (self.Tail - 1) : (self.Length - 1));
        self.Buffer[self.Tail] = inputSum;

        for (byte channel = 0; channel < (byte)OutputChannel.Count; ++channel) {
            byte gb = (byte)((byte)Reg.Gl1 + (channel * (byte)DatasheetSpecs.GainLaneCount));
            short accum = 0;

            for (byte tap = 1; tap < (byte)DatasheetSpecs.TapCount; ++tap) {
                ushort t = (ushort)(self.Tail + self.Taps[tap]);
                ushort head = (ushort)((t >= self.Length) ? (t - self.Length) : t);
                short buffered = self.Buffer[head];
                short g = self.Gains[gb + tap - 1];
                short bufferedG = MulShort(buffered, g);
                accum += bufferedG;
            }

            short total = accum;
            short v = self.Gains[(int)Reg.Vl + channel];
            short totalV = MulShort(total, v);
            short oversampled = (short)(totalV / (short)DatasheetSpecs.Oversampling);
            data.Outputs[channel] = oversampled;
        }
    }

    public static void ChipShortWrite(
        ref ChipShort self,
        byte address,
        byte data) {
        if (address < (byte)Reg.C0) {
            self.Regs[address] = (byte)(data & (byte)DatasheetSpecs.GainDataMask);
            short gain = RegisterToGainShort(data);
            self.Gains[address] = gain;
        } else if (address < (byte)Reg.T0) {
            self.Regs[address] = (byte)(data & (byte)DatasheetSpecs.CoeffValueMask);
            short coeff = RegisterToCoeffShort(data);
            self.Gains[address] = coeff;
        } else if (address < (byte)Reg.Count) {
            self.Regs[address] = (byte)(data & (byte)DatasheetSpecs.TapValueMask);
            ushort tap = RegisterToTapIdeal(data, self.SampleRate);
            self.Taps[address - (byte)Reg.T0] = tap;
        }
    }

    public static void ChipShortSetup(
        ref ChipShort self,
        ushort sampleRate
    ) {
        if (self.SampleRate != sampleRate) {
            self.SampleRate = sampleRate;

            if (self.Length > 0) {
                self.Buffer = Array.Empty<short>();
            }

            if (sampleRate >= 10) {
                int length = (sampleRate / 10) + 1;
                self.Buffer = new short[length];

                for (byte i = 0; i < (byte)DatasheetSpecs.TapCount; ++i) {
                    byte data = self.Regs[i + (byte)Reg.T0];
                    self.Taps[i] = RegisterToTapIdeal(data, self.SampleRate);
                }
            } else {
                self.Buffer = Array.Empty<short>();
            }
        }
    }
}