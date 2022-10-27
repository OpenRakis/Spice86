namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public static partial class YM7128B {
    enum YM7128B_ImplementationSpecs {
        // Fixed point specs
        YM7128B_Fixed_Bits = sizeof(short) * 8,
        YM7128B_Fixed_Mask = (1 << YM7128B_Fixed_Bits) - 1,
        YM7128B_Fixed_Decimals = YM7128B_Fixed_Bits - 1,
        YM7128B_Fixed_Rounding = 1 << (YM7128B_Fixed_Decimals - 1),
        YM7128B_Fixed_Max = (1 << YM7128B_Fixed_Decimals) - 1,
        YM7128B_Fixed_Min = -YM7128B_Fixed_Max,

        // Signal specs
        YM7128B_Signal_Bits = 14,
        YM7128B_Signal_Clear_Bits = YM7128B_Fixed_Bits - YM7128B_Signal_Bits,
        YM7128B_Signal_Clear_Mask = (1 << YM7128B_Signal_Clear_Bits) - 1,
        YM7128B_Signal_Mask = YM7128B_Fixed_Mask - YM7128B_Signal_Clear_Mask,
        YM7128B_Signal_Decimals = YM7128B_Signal_Bits - 1,

        // Signal multiplication operand specs
        YM7128B_Operand_Bits = YM7128B_Fixed_Bits,//TBV 14,
        YM7128B_Operand_Clear_Bits = YM7128B_Fixed_Bits - YM7128B_Operand_Bits,
        YM7128B_Operand_Clear_Mask = (1 << YM7128B_Operand_Clear_Bits) - 1,
        YM7128B_Operand_Mask = YM7128B_Fixed_Mask - YM7128B_Operand_Clear_Mask,
        YM7128B_Operand_Decimals = YM7128B_Operand_Bits - 1,

        // Gain multiplication operand specs
        YM7128B_Gain_Bits = 12,
        YM7128B_Gain_Clear_Bits = YM7128B_Fixed_Bits - YM7128B_Gain_Bits,
        YM7128B_Gain_Clear_Mask = (1 << YM7128B_Gain_Clear_Bits) - 1,
        YM7128B_Gain_Mask = YM7128B_Fixed_Mask - YM7128B_Gain_Clear_Mask,
        YM7128B_Gain_Decimals = YM7128B_Gain_Bits - 1,
        YM7128B_Gain_Max = (1 << (YM7128B_Fixed_Bits - 1)) - 1,
        YM7128B_Gain_Min = -YM7128B_Gain_Max,

        // Feedback coefficient multiplication operand specs
        YM7128B_Coeff_Bits = YM7128B_Gain_Bits,
        YM7128B_Coeff_Clear_Bits = YM7128B_Fixed_Bits - YM7128B_Coeff_Bits,
        YM7128B_Coeff_Clear_Mask = (1 << YM7128B_Coeff_Clear_Bits) - 1,
        YM7128B_Coeff_Mask = YM7128B_Fixed_Mask - YM7128B_Coeff_Clear_Mask,
        YM7128B_Coeff_Decimals = YM7128B_Coeff_Bits - 1
    };
}