namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

using System.Text.Json.Serialization;

/// <summary>
/// Base class for serializable breakpoint data.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SerializedBreakpoint), typeDiscriminator: "breakpoint")]
[JsonDerivedType(typeof(SerializedBreakpointRange), typeDiscriminator: "range")]
public record SerializedBreakpoint {
    /// <summary>
    /// Gets the format version.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// Gets the trigger value for the breakpoint.
    /// </summary>
    public long Trigger { get; init; }

    /// <summary>
    /// Gets the type of the breakpoint.
    /// </summary>
    public BreakPointType Type { get; init; }

    /// <summary>
    /// Gets a value indicating whether the breakpoint should be removed after being triggered.
    /// </summary>
    public bool IsRemovedOnTrigger { get; init; }

    /// <summary>
    /// Gets the comment for the breakpoint.
    /// </summary>
    public string Comment { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the breakpoint is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the condition expression for the breakpoint.
    /// </summary>
    /// <remarks>
    /// Reserved for future use. This would allow expressions like "eax == 0x1234" 
    /// to be evaluated when the breakpoint address is hit.
    /// </remarks>
    public string? Condition { get; init; }
}
