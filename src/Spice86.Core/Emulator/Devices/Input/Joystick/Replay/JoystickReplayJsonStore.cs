namespace Spice86.Core.Emulator.Devices.Input.Joystick.Replay;

using Serilog.Events;

using Spice86.Shared.Emulator.Input.Joystick.Replay;
using Spice86.Shared.Interfaces;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// <see cref="IJoystickReplayStore"/> implementation backed by a
/// human-readable JSON document. Used by tests, the headless host
/// and the future mapper UI to feed deterministic joystick event
/// sequences back into the emulator.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Mapping.JoystickMappingJsonStore"/>: enums are
/// written as strings, output is indented for hand-editing, unknown
/// JSON properties are ignored, and schema versions the running
/// build does not understand are rejected with a warning matching
/// DOSBox Staging's <c>"JOYSTICK: invalid replay entry"</c> tone.
/// </remarks>
public sealed class JoystickReplayJsonStore : IJoystickReplayStore {
    /// <summary>
    /// Highest schema version this build can deserialize. When
    /// <see cref="JoystickReplayScript.SchemaVersion"/> is greater
    /// than this value, the file is rejected and a warning is
    /// logged so the user knows to upgrade Spice86.
    /// </summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = BuildOptions();

    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new <see cref="JoystickReplayJsonStore"/>.
    /// </summary>
    /// <param name="loggerService">Logger used for the warning
    /// emitted when a file is missing, malformed or carries a
    /// future schema version.</param>
    public JoystickReplayJsonStore(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    /// <inheritdoc />
    public JoystickReplayScript? Load(string path) {
        if (!File.Exists(path)) {
            return null;
        }
        string json;
        try {
            json = File.ReadAllText(path);
        } catch (IOException ex) {
            LogInvalidEntry(path, ex.Message);
            return null;
        } catch (UnauthorizedAccessException ex) {
            LogInvalidEntry(path, ex.Message);
            return null;
        }
        JoystickReplayScript? script;
        try {
            script = JsonSerializer.Deserialize<JoystickReplayScript>(json, Options);
        } catch (JsonException ex) {
            LogInvalidEntry(path, ex.Message);
            return null;
        }
        if (script is null) {
            LogInvalidEntry(path, "document is empty");
            return null;
        }
        if (script.SchemaVersion > SupportedSchemaVersion) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(
                    "JOYSTICK: invalid replay entry in {Path}: schema version {Version} is newer than supported {Supported}",
                    path, script.SchemaVersion, SupportedSchemaVersion);
            }
            return null;
        }
        return script;
    }

    /// <inheritdoc />
    public void Save(string path, JoystickReplayScript script) {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }
        string json = JsonSerializer.Serialize(script, Options);
        File.WriteAllText(path, json);
    }

    private void LogInvalidEntry(string path, string reason) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning(
                "JOYSTICK: invalid replay entry in {Path}: {Reason}",
                path, reason);
        }
    }

    private static JsonSerializerOptions BuildOptions() {
        JsonSerializerOptions options = new() {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
