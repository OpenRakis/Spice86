namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Settings that route gameport writes (DOS-side I/O on port
/// <c>0x201</c>) onto the MPU-401 MIDI device, mirroring DOSBox
/// Staging's gameport-MIDI feature for games that drive a MIDI
/// adapter through the gameport instead of through the dedicated
/// MPU-401 ports.
/// </summary>
public sealed class MidiOnGameportSettings {

    /// <summary>
    /// When <see langword="true"/>, gameport accesses are forwarded
    /// to the MPU-401 device in addition to the normal
    /// joystick-decay logic. Disabled by default.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional override of the destination MPU-401 base port. When
    /// <see langword="null"/>, the default <c>0x330</c> is used.
    /// </summary>
    public int? Mpu401BasePort { get; set; }
}
