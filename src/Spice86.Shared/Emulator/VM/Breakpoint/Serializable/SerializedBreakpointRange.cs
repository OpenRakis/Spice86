namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
/// <summary>
/// Class for serializing range breakpoint data.
/// </summary>
public record SerializedBreakpointRange : SerializedBreakpoint
{
    /// <summary>
    /// Gets or sets the end trigger value for the range breakpoint.
    /// </summary>
    public long EndTrigger { get; init; }
}
