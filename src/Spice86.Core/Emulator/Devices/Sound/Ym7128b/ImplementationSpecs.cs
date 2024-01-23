namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
internal enum ImplementationSpecs {
    // Fixed point specs
    FixedBits = sizeof(short) * 8,
    FixedMask = (1 << FixedBits) - 1,
    FixedDecimals = FixedBits - 1,
    FixedRounding = 1 << (FixedDecimals - 1),
    FixedMax = (1 << FixedDecimals) - 1,
    FixedMin = -FixedMax,

    // Signal specs
    SignalBits = 14,
    SignalClearBits = FixedBits - SignalBits,
    SignalClearMask = (1 << SignalClearBits) - 1,
    SignalMask = FixedMask - SignalClearMask,
    SignalDecimals = SignalBits - 1,

    // Signal multiplication operand specs
    OperandBits = FixedBits,//TBV 14,
    OperandClearBits = FixedBits - OperandBits,
    OperandClearMask = (1 << OperandClearBits) - 1,
    OperandMask = FixedMask - OperandClearMask,
    OperandDecimals = OperandBits - 1,

    // Gain multiplication operand specs
    GainBits = 12,
    GainClearBits = FixedBits - GainBits,
    GainClearMask = (1 << GainClearBits) - 1,
    GainMask = FixedMask - GainClearMask,
    Ym7128BGainDecimals = GainBits - 1,
    GainMax = (1 << (FixedBits - 1)) - 1,
    GainMin = -GainMax,

    // Feedback coefficient multiplication operand specs
    CoeffBits = GainBits,
    CoeffClearBits = FixedBits - CoeffBits,
    CoeffClearMask = (1 << CoeffClearBits) - 1,
    CoeffMask = FixedMask - CoeffClearMask,
    CoeffDecimals = CoeffBits - 1
};
