namespace Spice86.Shared.Emulator.Input.Joystick.Replay;

/// <summary>
/// Persistence interface for joystick replay scripts. Mirrors the
/// shape of <see cref="Mapping.IJoystickMappingStore"/> so the UI,
/// headless host and tests can share one contract over the JSON
/// store while keeping deserialization out of the Core.
/// </summary>
public interface IJoystickReplayStore {
    /// <summary>
    /// Loads the script at the given file path. Returns
    /// <see langword="null"/> when the file does not exist or
    /// cannot be parsed; implementations log a warning matching
    /// the <c>"JOYSTICK: invalid replay entry"</c> message in the
    /// malformed case.
    /// </summary>
    /// <param name="path">Absolute path to the JSON document.</param>
    /// <returns>The deserialized script, or <see langword="null"/>.</returns>
    JoystickReplayScript? Load(string path);

    /// <summary>
    /// Persists the given script to disk at the given path,
    /// overwriting any existing file.
    /// </summary>
    /// <param name="path">Absolute path to the JSON document.</param>
    /// <param name="script">Script to serialize.</param>
    void Save(string path, JoystickReplayScript script);
}
