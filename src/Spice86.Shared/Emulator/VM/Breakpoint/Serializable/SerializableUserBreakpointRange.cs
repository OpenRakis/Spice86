namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

/// <summary>
/// Class for serializing memory range breakpoints
/// </summary>
public record SerializableUserBreakpointRange : SerializableUserBreakpoint {
    /// <summary>
    /// Gets the end trigger value for the range breakpoint.
    /// </summary>
    public long EndTrigger { get; init; }
}
