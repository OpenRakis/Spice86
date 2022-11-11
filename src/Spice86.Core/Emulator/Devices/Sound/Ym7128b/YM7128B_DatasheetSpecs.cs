namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
//! Datasheet specifications
enum YM7128B_DatasheetSpecs {
    //! Clock rate [Hz]
    YM7128B_Clock_Rate = 7159090,

    //! Register write rate [Hz]
    YM7128B_Write_Rate = (YM7128B_Clock_Rate / 8) / (8 + 1 + 8 + 1),

    //! Input sample rate [Hz]
    YM7128B_Input_Rate = (YM7128B_Clock_Rate + (304 / 2)) / 304,

    //! Output oversampling factor
    YM7128B_Oversampling = 2,

    //! Output sample rate
    YM7128B_Output_Rate = YM7128B_Input_Rate * YM7128B_Oversampling,

    //! Maximum register address
    YM7128B_Address_Max = YM7128B_Reg.YM7128B_Reg_Count - 1,

    //! Nominal delay line buffer length
    YM7128B_Buffer_Length = (YM7128B_Input_Rate / 10) + 1,

    // Delay line taps
    YM7128B_Tap_Count = 9,
    YM7128B_Tap_Value_Bits = 5,
    YM7128B_Tap_Value_Count = 1 << YM7128B_Tap_Value_Bits,
    YM7128B_Tap_Value_Mask = YM7128B_Tap_Value_Count - 1,

    // Gain coefficients
    YM7128B_Gain_Lane_Count = 8,
    YM7128B_Gain_Data_Bits = 6,
    YM7128B_Gain_Data_Count = 1 << YM7128B_Gain_Data_Bits,
    YM7128B_Gain_Data_Mask = YM7128B_Gain_Data_Count - 1,
    YM7128B_Gain_Data_Sign = 1 << (YM7128B_Gain_Data_Bits - 1),

    // Feedback coefficients
    YM7128B_Coeff_Count = 2,
    YM7128B_Coeff_Value_Bits = 6,
    YM7128B_Coeff_Value_Count = 1 << YM7128B_Coeff_Value_Bits,
    YM7128B_Coeff_Value_Mask = YM7128B_Coeff_Value_Count - 1
}
