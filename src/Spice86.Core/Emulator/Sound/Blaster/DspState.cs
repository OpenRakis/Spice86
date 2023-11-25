namespace Spice86.Core.Emulator.Sound.Blaster;

/// <summary>
/// All the possible states the SoundBlaster DSP can be in.
/// </summary>
public enum DspState : byte {
    /// <summary>
    /// The DSP was reset.
    /// </summary>
    Reset,
    /// <summary>
    /// The DSP is awaiting a Reset
    /// </summary>
    ResetWait,
    /// <summary>
    /// The DSP is available
    /// </summary>
    Normal

}
