namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
/// Specifies which register to read from when using the CommandRead or DataRead method.
/// </summary>
enum SelectedReadRegister {
    InServiceRegister, InterruptRequestRegister
}