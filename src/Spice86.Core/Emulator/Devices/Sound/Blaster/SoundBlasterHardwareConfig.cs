namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
///     Depending on the SoundBlaster variant, these can change.
/// </summary>
/// <param name="Irq">Default is 7.</param>
/// <param name="LowDma">Default is 1.</param>
/// <param name="HighDma">Default is 5.</param>
/// <param name="SbType">The type of SoundBlaster card to emulate.</param>
/// <param name="BaseAddress">The base I/O address of the Sound Blaster card (defaults to 0x220).</param>
public record SoundBlasterHardwareConfig(
    byte Irq, byte LowDma, byte HighDma, SbType SbType, ushort BaseAddress = 0x220);