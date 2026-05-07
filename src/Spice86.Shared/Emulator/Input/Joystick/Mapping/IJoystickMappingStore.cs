namespace Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>
/// Persistence interface for joystick mapping documents. Lets the
/// UI, headless host and tests share one contract over the JSON
/// store while keeping the deserialization logic out of the Core.
/// </summary>
public interface IJoystickMappingStore {

    /// <summary>
    /// Loads the mapping at the given file path. Returns
    /// <see langword="null"/> if the file does not exist or cannot
    /// be parsed; implementations log a warning matching DOSBox
    /// Staging's <c>"JOYSTICK: invalid mapper entry"</c> message in
    /// the malformed case.
    /// </summary>
    /// <param name="path">Absolute path to the JSON document.</param>
    /// <returns>The deserialized mapping, or <see langword="null"/>.</returns>
    JoystickMapping? Load(string path);

    /// <summary>
    /// Persists the given mapping to disk at the given path,
    /// overwriting any existing file.
    /// </summary>
    /// <param name="path">Absolute path to the JSON document.</param>
    /// <param name="mapping">Mapping to serialize.</param>
    void Save(string path, JoystickMapping mapping);
}
