namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

/// <summary>
/// Represents a collection of breakpoints associated with a specific program, identified by its SHA256 hash.
/// </summary>
public record ProgramSerializedBreakpoints {
    /// <summary>
    /// Gets the SHA256 hash of the program these breakpoints belong to.
    /// </summary>
    public required string ProgramHash { get; init; }

    /// <summary>
    /// Gets the format version of the serialized breakpoints collection.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// All the breakpoints that are present in the internal Spice86 debugger, serialized when Spice86 exits.
    /// </summary>
    public required SerializedBreakpoints SerializedBreakpoints { get; init; }
}
