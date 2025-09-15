namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

using System.Text.Json.Serialization;

/// <summary>
/// Base class for serializable breakpoint data crated by the user in the internal Spice86 debugger.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SerializableUserBreakpoint), typeDiscriminator: "breakpoint")]
[JsonDerivedType(typeof(SerializableUserBreakpointRange), typeDiscriminator: "range")]
public record SerializableUserBreakpoint {
    /// <summary>
    /// Gets the trigger value for the breakpoint.
    /// </summary>
    public long Trigger { get; init; }

    /// <summary>
    /// Gets the type of the breakpoint.
    /// </summary>
    public BreakPointType Type { get; init; }

    /// <summary>
    /// Gets a value indicating whether the breakpoint is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }
}
