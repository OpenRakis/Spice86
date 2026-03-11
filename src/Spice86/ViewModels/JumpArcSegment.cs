namespace Spice86.ViewModels;

/// <summary>
/// Represents a segment of a jump arc that passes through a specific disassembly line.
/// </summary>
/// <param name="Lane">The lane index (0 = rightmost/closest to code, higher = further left).</param>
/// <param name="Type">Whether this line is the top end, bottom end, or middle of the arc.</param>
/// <param name="IsTarget">Whether the arrowhead points at this line (the jump destination).</param>
public record JumpArcSegment(int Lane, JumpSegmentType Type, bool IsTarget);
