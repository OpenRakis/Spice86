namespace Spice86.Core.Emulator.Devices.Sound.Midi;

/// <summary>
/// The status of the General MIDI device.
/// </summary>
[Flags]
public enum GeneralMidiStatus : byte {
    /// <summary>
    /// The status of the device is unknown.
    /// </summary>
    None = 0,
    /// <summary>
    /// The command port may be written to.
    /// </summary>
    OutputReady = 1 << 6,
    /// <summary>
    /// The data port may be read from.
    /// </summary>
    InputReady = 1 << 7
}