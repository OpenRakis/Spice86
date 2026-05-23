namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

using System.Collections.Generic;

/// <summary>
/// One device profile inside a <see cref="JoystickMapping"/>. A
/// profile binds a specific physical controller (matched by SDL
/// <see cref="DeviceGuid"/> first, then by <see cref="DeviceName"/>)
/// to logical gameport behavior: which axes/buttons/hat to forward,
/// the gameport personality to expose, deadzone, rumble and the
/// MIDI-on-gameport routing.
/// </summary>
/// <remarks>
/// Profiles are auto-loaded from the user-configured profiles
/// directory. The first profile whose <see cref="DeviceGuid"/>
/// matches the connected SDL device wins; if no GUID match is found,
/// a name-based match is attempted. If still nothing matches the
/// embedded default Xbox-controller profile is used.
/// </remarks>
public sealed class JoystickProfile {

    /// <summary>
    /// Friendly name of the profile (e.g.
    /// <c>"Xbox 360 Controller"</c>) shown in the mapper UI.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SDL joystick GUID (32-character hex string, lowercase) used
    /// for primary auto-load matching. Empty means "match by name
    /// only".
    /// </summary>
    public string DeviceGuid { get; set; } = string.Empty;

    /// <summary>
    /// Substring matched (case-insensitive) against
    /// <c>SDL_JoystickName</c> when <see cref="DeviceGuid"/> does not
    /// match. Empty means "no name match".
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Gameport personality this profile should select.
    /// </summary>
    public JoystickType Type { get; set; } = JoystickType.Auto;

    /// <summary>
    /// Profile-wide deadzone in <c>[0, 100]</c> percent. Mirrors
    /// DOSBox Staging's <c>deadzone</c> setting.
    /// </summary>
    public int DeadzonePercent { get; set; } = 10;

    /// <summary>
    /// Whether circular rather than square axis transformation is
    /// used. Mirrors DOSBox Staging's <c>JOYMAP_CIRCLE</c> /
    /// <c>JOYMAP_SQUARE</c> options.
    /// </summary>
    public bool UseCircularDeadzone { get; set; }

    /// <summary>
    /// Whether stick B's X and Y axes should be swapped before
    /// being sent to the gameport. Mirrors DOSBox Staging's
    /// <c>swap34</c> flag.
    /// </summary>
    public bool SwapStickBAxes { get; set; }

    /// <summary>Per-axis bindings.</summary>
    public List<AxisMapping> Axes { get; set; } = new();

    /// <summary>Per-button bindings.</summary>
    public List<ButtonMapping> Buttons { get; set; } = new();

    /// <summary>Hat (POV) binding.</summary>
    public HatMapping Hat { get; set; } = new();

    /// <summary>Rumble settings for this profile.</summary>
    public RumbleMapping Rumble { get; set; } = new();

    /// <summary>MIDI-on-gameport settings for this profile.</summary>
    public MidiOnGameportSettings MidiOnGameport { get; set; } = new();
}
