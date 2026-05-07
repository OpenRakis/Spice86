namespace Spice86.Shared.Emulator.Input.Joystick.Replay;

using System.Collections.Generic;

/// <summary>
/// Top-level document describing a deterministic sequence of
/// joystick events to feed back into the emulator.
/// </summary>
/// <remarks>
/// Replay scripts are useful for headless integration tests, MCP
/// smoke tests, and the future mapper UI's "test profile" feature.
/// They are JSON-serialized via
/// <see cref="IJoystickReplayStore"/>. The schema is intentionally
/// narrow: a flat list of timestamped steps, no looping, no
/// branching. Fancier scenarios should be expressed by chaining
/// several scripts at the host level.
/// </remarks>
public sealed class JoystickReplayScript {
    /// <summary>
    /// Document schema version. Newer files written against a
    /// future schema are rejected by the store with a warning so
    /// the user knows to upgrade Spice86.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Optional friendly name surfaced in logs and the mapper UI.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of steps. The player processes them in this
    /// exact order; <see cref="JoystickReplayStep.DelayMs"/> is
    /// always relative to the previous step.
    /// </summary>
    public List<JoystickReplayStep> Steps { get; set; } = new();
}
