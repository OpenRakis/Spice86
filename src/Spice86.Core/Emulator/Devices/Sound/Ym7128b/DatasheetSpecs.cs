namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
//! Datasheet specifications
enum DatasheetSpecs {
    //! Clock rate [Hz]
    ClockRate = 7159090,

    //! Register write rate [Hz]
    WriteRate = (ClockRate / 8) / (8 + 1 + 8 + 1),

    //! Input sample rate [Hz]
    InputRate = (ClockRate + (304 / 2)) / 304,

    //! Output oversampling factor
    Oversampling = 2,

    //! Output sample rate
    OutputRate = InputRate * Oversampling,

    //! Maximum register address
    AddressMax = Reg.Count - 1,

    //! Nominal delay line buffer length
    BufferLength = (InputRate / 10) + 1,

    // Delay line taps
    TapCount = 9,
    TapValueBits = 5,
    TapValueCount = 1 << TapValueBits,
    TapValueMask = TapValueCount - 1,

    // Gain coefficients
    GainLaneCount = 8,
    GainDataBits = 6,
    GainDataCount = 1 << GainDataBits,
    GainDataMask = GainDataCount - 1,
    GainDataSign = 1 << (GainDataBits - 1),

    // Feedback coefficients
    CoeffCount = 2,
    CoeffValueBits = 6,
    CoeffValueCount = 1 << CoeffValueBits,
    CoeffValueMask = CoeffValueCount - 1
}
