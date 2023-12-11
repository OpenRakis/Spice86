namespace Spice86.Core.Emulator.VM.Pause;
/// <summary>
/// Exposes a property to pause emulation
/// </summary>
public interface IPauseable {
    /// <summary>
    /// Gets or sets whether the emulation is paused
    /// </summary>
    bool IsPaused { get; set; }
}
