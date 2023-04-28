namespace Spice86.Aeon.Emulator.Video.Registers;

using Spice86.Aeon.Emulator.Video.Registers.General;

public class GeneralRegisters {
    /// <summary>
    ///     Get the Miscellaneous Output register.
    /// </summary>
    public MiscellaneousOutput MiscellaneousOutput { get; set; } = new();

    /// <summary>
    /// Get the Input Status Register 0.
    /// </summary>
    public InputStatusRegister0 InputStatusRegister0 { get; set; } = new();

    /// <summary>
    /// Get the Input Status Register 1.
    /// </summary>
    public InputStatusRegister1 InputStatusRegister1 { get; set; } = new();
}

public enum Polarity {
    Negative,
    Positive
}

public enum ClockSelect {
    Use25175Khz,
    Use28322Khz,
    External,
    Reserved
}

public enum IoAddressSelect {
    Monochrome,
    Color
}