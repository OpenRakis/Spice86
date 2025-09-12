namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
/// <summary>
/// Container class for a collection of serialized breakpoints.
/// </summary>
public record SerializedBreakpoints {
    /// <summary>
    /// Gets the format version.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// Gets the collection of serialized breakpoints.
    /// </summary>
    public IList<SerializedBreakpoint> Breakpoints { get; init; } = new List<SerializedBreakpoint>();
}