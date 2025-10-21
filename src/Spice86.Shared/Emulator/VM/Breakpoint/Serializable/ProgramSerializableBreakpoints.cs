namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

/// <summary>
/// Represents a collection of breakpoints associated with a specific program, identified by its SHA256 hash.
/// </summary>
public record ProgramSerializableBreakpoints {
    /// <summary>
    /// Gets the SHA256 hash of the program these breakpoints belong to.
    /// </summary>
    public required string ProgramHash { get; init; }

    /// <summary>
    /// Gets all the breakpoints that are present in the internal Spice86 debugger, serialized when Spice86 exits.
    /// </summary>
    public required SerializableUserBreakpointCollection SerializedBreakpoints { get; init; }
}
