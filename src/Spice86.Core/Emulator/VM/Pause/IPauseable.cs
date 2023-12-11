namespace Spice86.Core.Emulator.VM.Pause;
/// <summary>
/// Represents an emulator class with a render thread that can be paused.
/// </summary>
public interface IPauseable {
    /// <summary>
    /// Gets or sets whether the emulation is paused
    /// </summary>
    bool IsPaused { get; set; }
}
