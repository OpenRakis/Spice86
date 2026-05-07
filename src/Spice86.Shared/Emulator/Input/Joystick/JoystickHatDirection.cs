namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Eight-way digital hat (POV) direction carried in
/// <see cref="JoystickHatEventArgs"/>. Matches the SDL
/// <c>SDL_HAT_*</c> bitmask values and DOSBox Staging's hat handling.
/// </summary>
public enum JoystickHatDirection {
    /// <summary>Hat is centred (no direction pressed).</summary>
    Centered = 0x00,

    /// <summary>Up (north).</summary>
    Up = 0x01,

    /// <summary>Right (east).</summary>
    Right = 0x02,

    /// <summary>Down (south).</summary>
    Down = 0x04,

    /// <summary>Left (west).</summary>
    Left = 0x08,

    /// <summary>Up-right diagonal (north-east).</summary>
    UpRight = Up | Right,

    /// <summary>Up-left diagonal (north-west).</summary>
    UpLeft = Up | Left,

    /// <summary>Down-right diagonal (south-east).</summary>
    DownRight = Down | Right,

    /// <summary>Down-left diagonal (south-west).</summary>
    DownLeft = Down | Left
}
