namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Depending on the SoundBlaster variant, these can change.
/// </summary>
/// <param name="Irq">Defaults is 7.</param>
/// <param name="LowDma">Defaults is 1.</param>
/// <param name="HighDma">Default is 5.</param>
public record SoundBlasterHardwareConfig(byte Irq, byte LowDma, byte HighDma);