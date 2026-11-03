namespace Spice86.Core.Emulator.Devices.Sound.Tandy;

/// <summary>Controls automatic, complete, PSG-only, or disabled Tandy sound.</summary>
public enum TandySoundMode {
    /// <summary>Enable native sound for a Tandy machine.</summary>
    Auto,
    /// <summary>Enable PSG and DAC.</summary>
    On,
    /// <summary>Enable only the PSG, allowing Sound Blaster BIOS routing.</summary>
    Psg,
    /// <summary>Disable Tandy sound and its BIOS services.</summary>
    Off
}