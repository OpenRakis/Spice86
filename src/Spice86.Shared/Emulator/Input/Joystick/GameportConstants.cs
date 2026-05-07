namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Hardware constants for IBM PC gameport (I/O port <c>0x201</c>)
/// emulation, taken from DOSBox Staging
/// (<c>src/hardware/input/joystick.cpp</c>).
/// </summary>
/// <remarks>
/// The original IBM gameport schematic uses a 558 quad-timer
/// configured as four monostable one-shots. Each one-shot's pulse
/// width is <c>R*C</c> where <c>C = 0.01 µF</c> and <c>R</c> is the
/// stick's potentiometer resistance (0..120 kΩ). Reading port
/// <c>0x201</c> returns the four pulse-active bits in the low nibble
/// and four button bits in the high nibble. A write to the port
/// arms (re-triggers) all four one-shots.
/// </remarks>
public static class GameportConstants {

    /// <summary>
    /// I/O port number reserved for the IBM PC gameport.
    /// </summary>
    public const int Port201 = 0x201;

    /// <summary>
    /// Range constant used by DOSBox Staging's tick-counted (legacy)
    /// gameport read path. <c>RANGE</c> in
    /// <c>src/hardware/input/joystick.cpp</c>.
    /// </summary>
    public const int LegacyRange = 64;

    /// <summary>
    /// Reset window in PIC ticks (≈ ms) after which the legacy
    /// counted read path zeroes its counters if no new write has
    /// arrived. <c>TIMEOUT</c> in DOSBox Staging.
    /// </summary>
    public const int LegacyResetTimeoutTicks = 10;

    /// <summary>
    /// Calibration scalar applied to the X axis when computing the
    /// pulse-width deadline in PIC milliseconds. Derived from the
    /// joycheck measurements quoted in
    /// <c>src/hardware/input/joystick.cpp::write_p201_timed</c>:
    /// <c>1.112 / 2</c>.
    /// </summary>
    public const double DefaultAxisXScalar = 1.112 / 2.0;

    /// <summary>
    /// Calibration scalar applied to the Y axis. Derived from the
    /// joycheck measurements: <c>1.110 / 2</c>.
    /// </summary>
    public const double DefaultAxisYScalar = 1.110 / 2.0;

    /// <summary>
    /// Constant offset (in PIC milliseconds) added to every axis
    /// pulse-width deadline.
    /// </summary>
    public const double DefaultAxisOffsetMs = 0.02;

    /// <summary>
    /// Bit mask for stick A X axis active. Cleared in the byte
    /// returned from <see cref="Port201"/> while the X one-shot is
    /// still firing.
    /// </summary>
    public const byte BitStickAxAxis = 0x01;

    /// <summary>Bit mask for stick A Y axis active.</summary>
    public const byte BitStickAyAxis = 0x02;

    /// <summary>Bit mask for stick B X axis active.</summary>
    public const byte BitStickBxAxis = 0x04;

    /// <summary>Bit mask for stick B Y axis active.</summary>
    public const byte BitStickByAxis = 0x08;

    /// <summary>Bit mask for stick A button 1 pressed.</summary>
    public const byte BitStickAButton1 = 0x10;

    /// <summary>Bit mask for stick A button 2 pressed.</summary>
    public const byte BitStickAButton2 = 0x20;

    /// <summary>Bit mask for stick B button 1 pressed.</summary>
    public const byte BitStickBButton1 = 0x40;

    /// <summary>Bit mask for stick B button 2 pressed.</summary>
    public const byte BitStickBButton2 = 0x80;

    /// <summary>
    /// Default value returned from <see cref="Port201"/> when no
    /// stick is enabled and no axis is firing. All bits set means
    /// "no axis pulse, no button pressed" on the IBM gameport.
    /// </summary>
    public const byte UnpluggedReading = 0xFF;
}
