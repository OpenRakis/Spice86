namespace Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;

using Spice86.Shared.Emulator.Input.Joystick.Mapping;

using System.Collections.Generic;

/// <summary>
/// Result of <see cref="JoystickProfileAutoLoader.LoadAll(string?)"/>:
/// every profile read from disk, in deterministic order, plus the
/// first non-empty <see cref="JoystickMapping.DefaultProfileName"/>
/// encountered.
/// </summary>
/// <param name="Profiles">All profiles, in load order.</param>
/// <param name="DefaultProfileName">Fallback profile name to use
/// when GUID/name matching does not succeed. Empty when no file
/// declared one.</param>
public readonly record struct LoadedProfiles(
    IReadOnlyList<JoystickProfile> Profiles,
    string DefaultProfileName);
