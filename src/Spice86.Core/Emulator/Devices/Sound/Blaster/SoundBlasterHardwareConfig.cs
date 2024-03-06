namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Depending on the SoundBlaster variant, these can change.
/// </summary>
/// <param name="Irq">Defaults is 7.</param>
/// <param name="LowDma">Defaults is 1.</param>
/// <param name="HighDma">Default is 5.</param>
/// <param name="SbType">The type of SoundBlaster card to emulate. Defaults to SoundBlaster 16.</param>
public record SoundBlasterHardwareConfig(byte Irq, byte LowDma, byte HighDma, SbType SbType);