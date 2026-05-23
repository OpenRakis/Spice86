namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

using System.Collections.Generic;

/// <summary>
/// Root POCO for the on-disk JSON joystick mapper file. Lives in
/// <see cref="Spice86.Shared"/> so that the emulator core, headless
/// host and the Avalonia UI can all read and write the same
/// document without taking any UI dependency.
/// </summary>
/// <remarks>
/// The serialized form is consumed by
/// <c>JoystickProfileAutoLoader</c> at startup: the loader picks the
/// profile whose device GUID/name matches the connected SDL device,
/// or falls back to <see cref="DefaultProfileName"/>. Profiles are
/// scanned from <c>--JoystickProfilesDirectory</c> (one JSON file
/// per profile is also accepted).
/// </remarks>
public sealed class JoystickMapping {

    /// <summary>
    /// Schema version for forward compatibility. Bumped when a
    /// breaking schema change is introduced. The deserializer
    /// rejects unknown future versions with a warning log line and
    /// falls back to defaults.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Name of the profile in <see cref="Profiles"/> to apply when
    /// no GUID/name match is found. Empty means "use the embedded
    /// default Xbox-controller profile".
    /// </summary>
    public string DefaultProfileName { get; set; } = string.Empty;

    /// <summary>
    /// All known device profiles. Order is significant: the first
    /// matching profile is applied.
    /// </summary>
    public List<JoystickProfile> Profiles { get; set; } = new();
}
