namespace Spice86.Emulator.Sound.Blaster;

/// <summary>
/// Specifies the current state of the DSP command processor.
/// </summary>
internal enum BlasterState
{
    /// <summary>
    /// The DSP is ready to receive a command.
    /// </summary>
    WaitingForCommand,
    /// <summary>
    /// The DSP is waiting for all of a command's parameters to be written.
    /// </summary>
    ReadingCommand,
    /// <summary>
    /// A one has been written to the reset port.
    /// </summary>
    ResetRequest,
    /// <summary>
    /// The reset port has changed from 1 to 0 and the DSP is resetting.
    /// </summary>
    Resetting
}
