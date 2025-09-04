namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
/// <summary>
/// Class for serializing memory range breakpoints
/// </summary>
public record SerializedBreakpointRange : SerializedBreakpoint {
    /// <summary>
    /// Gets the end trigger value for the range breakpoint.
    /// </summary>
    public long EndTrigger { get; init; }
}
