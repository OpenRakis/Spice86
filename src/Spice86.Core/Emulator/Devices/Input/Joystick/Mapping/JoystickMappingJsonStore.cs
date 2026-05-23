namespace Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;

using Serilog.Events;

using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// <see cref="IJoystickMappingStore"/> implementation backed by a
/// human-readable JSON document. Used by the joystick auto-loader,
/// the mapper UI and the MCP server to persist per-controller
/// profiles.
/// </summary>
/// <remarks>
/// Enums are written as strings (matching DOSBox Staging's
/// text-config aesthetic), output is indented for hand-editing,
/// and unknown JSON properties are ignored so that older builds
/// can still load files written by newer ones. Schema versions the
/// running build does not understand are rejected with a warning.
/// </remarks>
public sealed class JoystickMappingJsonStore : IJoystickMappingStore {
    /// <summary>
    /// Highest schema version this build can deserialize. When
    /// <see cref="JoystickMapping.SchemaVersion"/> is greater than
    /// this value, the file is rejected and a warning is logged so
    /// the user knows to upgrade Spice86.
    /// </summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = BuildOptions();

    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new <see cref="JoystickMappingJsonStore"/>.
    /// </summary>
    /// <param name="loggerService">Logger used for the warning
    /// emitted when a file is missing, malformed or carries a
    /// future schema version.</param>
    public JoystickMappingJsonStore(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    /// <inheritdoc />
    public JoystickMapping? Load(string path) {
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
        JoystickMapping? mapping;
        try {
            mapping = JsonSerializer.Deserialize<JoystickMapping>(json, Options);
        } catch (JsonException ex) {
            LogInvalidEntry(path, ex.Message);
            return null;
        }
        if (mapping is null) {
            LogInvalidEntry(path, "document is empty");
            return null;
        }
        if (mapping.SchemaVersion > SupportedSchemaVersion) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning(
                    "JOYSTICK: invalid mapper entry in {Path}: schema version {Version} is newer than supported {Supported}",
                    path, mapping.SchemaVersion, SupportedSchemaVersion);
            }
            return null;
        }
        return mapping;
    }

    /// <inheritdoc />
    public void Save(string path, JoystickMapping mapping) {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }
        string json = JsonSerializer.Serialize(mapping, Options);
        File.WriteAllText(path, json);
    }

    private void LogInvalidEntry(string path, string reason) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning(
                "JOYSTICK: invalid mapper entry in {Path}: {Reason}",
                path, reason);
        }
    }

    private static JsonSerializerOptions BuildOptions() {
        JsonSerializerOptions options = new() {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
