namespace Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;

using Serilog.Events;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.IO;

/// <summary>
/// Scans a user-configured directory for joystick profile JSON
/// documents and resolves the best matching <see cref="JoystickProfile"/>
/// for a connected device.
/// </summary>
/// <remarks>
/// Match order, mirroring DOSBox Staging's mapper-file behavior:
/// <list type="number">
///   <item>Exact device GUID (case-insensitive).</item>
///   <item>Case-insensitive substring of the device name.</item>
///   <item>Profile named by any loaded
///     <see cref="JoystickMapping.DefaultProfileName"/>.</item>
///   <item>Embedded Xbox-360 default profile.</item>
/// </list>
/// Profiles are merged across all <c>*.json</c> files in the
/// directory; the per-file order and the lexicographic file order
/// together define a deterministic iteration order.
/// </remarks>
public sealed class JoystickProfileAutoLoader {
    /// <summary>
    /// Name of the embedded default profile returned when no other
    /// match is found. Public so tests and the mapper UI can refer
    /// to it without duplicating the literal.
    /// </summary>
    public const string EmbeddedDefaultProfileName = "Embedded Xbox 360 Controller";

    private readonly IJoystickMappingStore _store;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new <see cref="JoystickProfileAutoLoader"/>.
    /// </summary>
    /// <param name="store">JSON store used to deserialize each
    /// candidate file. Tests inject a fake.</param>
    /// <param name="loggerService">Logger used for the
    /// auto-load info/verbose messages.</param>
    public JoystickProfileAutoLoader(IJoystickMappingStore store, ILoggerService loggerService) {
        _store = store;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Loads every <c>*.json</c> file in <paramref name="directory"/>
    /// and returns the merged set of profiles plus the first non-empty
    /// <see cref="JoystickMapping.DefaultProfileName"/> seen.
    /// </summary>
    /// <param name="directory">Absolute path to the profile directory.
    /// A missing or empty directory yields an empty result.</param>
    /// <returns>The merged profile catalogue.</returns>
    public LoadedProfiles LoadAll(string? directory) {
        List<JoystickProfile> profiles = new();
        string defaultProfileName = string.Empty;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
            return new LoadedProfiles(profiles, defaultProfileName);
        }
        string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string file in files) {
            JoystickMapping? mapping = _store.Load(file);
            if (mapping is null) {
                continue;
            }
            if (defaultProfileName.Length == 0 && mapping.DefaultProfileName.Length > 0) {
                defaultProfileName = mapping.DefaultProfileName;
            }
            profiles.AddRange(mapping.Profiles);
        }
        return new LoadedProfiles(profiles, defaultProfileName);
    }

    /// <summary>
    /// Resolves the best matching profile for a connected device.
    /// Always returns a non-null profile thanks to the embedded
    /// default fallback.
    /// </summary>
    /// <param name="loaded">Result of a prior <see cref="LoadAll"/>
    /// call.</param>
    /// <param name="deviceGuid">SDL joystick GUID (32-character
    /// lowercase hex). Empty disables GUID matching.</param>
    /// <param name="deviceName">SDL joystick name. Empty disables
    /// name matching.</param>
    /// <returns>The best matching profile.</returns>
    public JoystickProfile Resolve(LoadedProfiles loaded, string deviceGuid, string deviceName) {
        JoystickProfile? match = MatchByGuid(loaded.Profiles, deviceGuid);
        if (match is not null) {
            LogAutoLoad(match, deviceName, "GUID");
            return match;
        }
        match = MatchByName(loaded.Profiles, deviceName);
        if (match is not null) {
            LogAutoLoad(match, deviceName, "name");
            return match;
        }
        match = MatchByProfileName(loaded.Profiles, loaded.DefaultProfileName);
        if (match is not null) {
            LogAutoLoad(match, deviceName, "DefaultProfileName");
            return match;
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose(
                "JOYSTICK: no profile matched device {DeviceName} ({DeviceGuid}); using embedded default",
                deviceName, deviceGuid);
        }
        return BuildEmbeddedDefaultProfile();
    }

    /// <summary>
    /// Builds the embedded fallback profile (a sensible Xbox 360
    /// controller mapping). Exposed so the mapper UI can show it
    /// as a starting point for new profiles.
    /// </summary>
    /// <returns>A fresh profile instance.</returns>
    public static JoystickProfile BuildEmbeddedDefaultProfile() {
        return new JoystickProfile {
            Name = EmbeddedDefaultProfileName,
            DeviceGuid = string.Empty,
            DeviceName = string.Empty,
            Type = JoystickType.TwoAxis,
            DeadzonePercent = 10,
            UseCircularDeadzone = true,
            SwapStickBAxes = false,
            Axes = new List<AxisMapping> {
                new() { RawAxisIndex = 0, Target = VirtualAxis.StickAX },
                new() { RawAxisIndex = 1, Target = VirtualAxis.StickAY },
                new() { RawAxisIndex = 2, Target = VirtualAxis.StickBX },
                new() { RawAxisIndex = 3, Target = VirtualAxis.StickBY }
            },
            Buttons = new List<ButtonMapping> {
                new() { RawButtonIndex = 0, Target = VirtualButton.StickAButton1 },
                new() { RawButtonIndex = 1, Target = VirtualButton.StickAButton2 },
                new() { RawButtonIndex = 2, Target = VirtualButton.StickBButton1 },
                new() { RawButtonIndex = 3, Target = VirtualButton.StickBButton2 }
            },
            Hat = new HatMapping { RawHatIndex = 0, TargetStickIndex = 1, Enabled = true },
            Rumble = new RumbleMapping { Enabled = true, AmplitudeScale = 1.0 },
            MidiOnGameport = new MidiOnGameportSettings { Enabled = false }
        };
    }

    private static JoystickProfile? MatchByGuid(IReadOnlyList<JoystickProfile> profiles, string deviceGuid) {
        if (string.IsNullOrEmpty(deviceGuid)) {
            return null;
        }
        foreach (JoystickProfile profile in profiles) {
            if (!string.IsNullOrEmpty(profile.DeviceGuid)
                && string.Equals(profile.DeviceGuid, deviceGuid, StringComparison.OrdinalIgnoreCase)) {
                return profile;
            }
        }
        return null;
    }

    private static JoystickProfile? MatchByName(IReadOnlyList<JoystickProfile> profiles, string deviceName) {
        if (string.IsNullOrEmpty(deviceName)) {
            return null;
        }
        foreach (JoystickProfile profile in profiles) {
            if (!string.IsNullOrEmpty(profile.DeviceName)
                && deviceName.Contains(profile.DeviceName, StringComparison.OrdinalIgnoreCase)) {
                return profile;
            }
        }
        return null;
    }

    private static JoystickProfile? MatchByProfileName(IReadOnlyList<JoystickProfile> profiles, string profileName) {
        if (string.IsNullOrEmpty(profileName)) {
            return null;
        }
        foreach (JoystickProfile profile in profiles) {
            if (string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase)) {
                return profile;
            }
        }
        return null;
    }

    private void LogAutoLoad(JoystickProfile profile, string deviceName, string reason) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "JOYSTICK: auto-loaded profile {ProfileName} for device {DeviceName} (matched by {Reason})",
                profile.Name, deviceName, reason);
        }
    }
}
