namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.General;

/// <summary>
/// Represents the general registers of the VGA.
/// </summary>
public class GeneralRegisters {
    /// <summary>
    ///     Get the Miscellaneous Output register.
    /// </summary>
    public MiscellaneousOutput MiscellaneousOutput { get; init; } = new();

    /// <summary>
    ///     Get the Input Status Register 0.
    /// </summary>
    public InputStatusRegister0 InputStatusRegister0 { get; init; } = new();

    /// <summary>
    ///     Get the Input Status Register 1.
    /// </summary>
    public InputStatusRegister1 InputStatusRegister1 { get; init; } = new();
}