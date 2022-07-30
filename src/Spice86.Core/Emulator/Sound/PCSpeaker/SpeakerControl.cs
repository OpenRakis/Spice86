namespace Spice86.Core.Emulator.Sound.PCSpeaker;

using System;

/// <summary>
/// Possible values of the PC speaker control register.
/// </summary>
[Flags]
internal enum SpeakerControl {
    /// <summary>
    /// The register is clear.
    /// </summary>
    Clear = 0,
    /// <summary>
    /// The PC speaker should use the system timer for input.
    /// </summary>
    UseTimer = 1,
    /// <summary>
    /// The PC speaker is on.
    /// </summary>
    SpeakerOn = 2
}
