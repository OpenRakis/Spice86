namespace Spice86.Shared.Emulator.VM.Breakpoint.Serializable;

using System.Text.Json.Serialization;

/// <summary>
/// Base class for serializable breakpoint data created by the user in the internal Spice86 debugger.
/// </summary>
/// <remarks>Not everything is serialized, this is why this is a different set of classes.</remarks>
public record SerializableUserBreakpoint {
    /// <summary>
    /// Gets the trigger value for the breakpoint.
    /// </summary>
    public long Trigger { get; init; }

    /// <summary>
    /// Gets the end trigger value for the range breakpoint.
    /// </summary>
    public long EndTrigger { get; init; }

    /// <summary>
    /// Gets the type of the breakpoint.
    /// </summary>
    public BreakPointType Type { get; init; }

    /// <summary>
    /// Gets a value indicating whether the breakpoint is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }
    
    /// <summary>
    /// Gets the condition expression string for conditional breakpoints.
    /// </summary>
    /// <remarks>When null or empty, the breakpoint is unconditional.</remarks>
    public string? ConditionExpression { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a wildcard (unconditional) breakpoint
    /// that fires on every event of the given <see cref="Type"/> regardless of address.
    /// </summary>
    /// <remarks>When true, <see cref="Trigger"/> and <see cref="EndTrigger"/> are not
    /// meaningful and the breakpoint is restored as a single unconditional breakpoint.</remarks>
    public bool IsWildcard { get; init; }
}
