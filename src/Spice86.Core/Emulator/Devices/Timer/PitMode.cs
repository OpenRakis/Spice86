namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Lists the programmable interval timer operating modes as encoded by the control word.
/// </summary>
public enum PitMode : byte {
    /// <summary>Counts down once and raises an interrupt when the terminal count is reached.</summary>
    InterruptOnTerminalCount = 0x0,

    /// <summary>Produces a hardware-triggerable one-shot pulse using the gate input.</summary>
    OneShot = 0x1,

    /// <summary>Continuously generates rate pulses by reloading the counter after each expiration.</summary>
    RateGenerator = 0x2,

    /// <summary>Produces a square wave, toggling the output at half the reload period.</summary>
    SquareWave = 0x3,

    /// <summary>Triggers a strobe when the terminal count is reached, using software gating.</summary>
    SoftwareStrobe = 0x4,

    /// <summary>Triggers a strobe when the terminal count is reached, using an external gate.</summary>
    HardwareStrobe = 0x5,

    /// <summary>Alias for <see cref="RateGenerator" /> as exposed by the three-bit mode field.</summary>
    RateGeneratorAlias = 0x6,

    /// <summary>Alias for <see cref="SquareWave" /> as exposed by the three-bit mode field.</summary>
    SquareWaveAlias = 0x7,

    /// <summary>Indicates an unprogrammed channel.</summary>
    Inactive
}
