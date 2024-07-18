namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.General;

public class GeneralRegisters {
    public GeneralRegisters(MiscellaneousOutput miscellaneousOutput, InputStatusRegister0 inputStatusRegister0, InputStatusRegister1 inputStatusRegister1) {
        MiscellaneousOutput = miscellaneousOutput;
        InputStatusRegister0 = inputStatusRegister0;
        InputStatusRegister1 = inputStatusRegister1;
    }
    
    /// <summary>
    ///     Get the Miscellaneous Output register.
    /// </summary>
    public MiscellaneousOutput MiscellaneousOutput { get; init; }

    /// <summary>
    ///     Get the Input Status Register 0.
    /// </summary>
    public InputStatusRegister0 InputStatusRegister0 { get; init; }

    /// <summary>
    ///     Get the Input Status Register 1.
    /// </summary>
    public InputStatusRegister1 InputStatusRegister1 { get; init; }
}