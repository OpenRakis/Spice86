namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

/// <summary>
/// Container class for a collection of user created serializable breakpoints, for the internal Spice86 debugger.
/// </summary>
public record SerializableUserBreakpointCollection {
    /// <summary>
    /// Gets the collection of serialized breakpoints.
    /// </summary>
    public IList<SerializableUserBreakpoint> Breakpoints { get; init; } = new List<SerializableUserBreakpoint>();
}