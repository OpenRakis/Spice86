namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// List of SoundBlaster types. Decides minor differences in the hardware.
/// </summary>
public enum SbType {
    /// <summary>
    /// SoundBlaster 16
    /// </summary>
    Sb16 = 6,
    /// <summary>
    /// SoundBlaster Pro
    /// </summary>
    SbPro1 = 2,

    /// <summary>
    /// SoundBlaster Pro 2
    /// </summary>
    SbPro2 = 4,
}
