namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
/// Floppy transfer speed presets.
/// </summary>
/// <remarks>
/// Ported from DOSBox Staging config file presets
/// </remarks>
public enum FloppyDiskSpeed {
    /// <summary>
    /// Do not add any transfer delay.
    /// </summary>
    Maximum,

    /// <summary>
    /// Emulate fast floppy transfers at 120 KB/s.
    /// </summary>
    Fast,

    /// <summary>
    /// Emulate medium floppy transfers at 60 KB/s.
    /// </summary>
    Medium,

    /// <summary>
    /// Emulate slow floppy transfers at 30 KB/s.
    /// </summary>
    Slow
}