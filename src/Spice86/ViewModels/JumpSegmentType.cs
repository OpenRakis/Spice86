namespace Spice86.ViewModels;

/// <summary>
/// Describes the role of a jump arc segment at a specific disassembly line.
/// </summary>
public enum JumpSegmentType {
    /// <summary>
    /// The topmost line of the arc (corner turning downward).
    /// </summary>
    TopEnd,

    /// <summary>
    /// The bottommost line of the arc (corner turning upward).
    /// </summary>
    BottomEnd,

    /// <summary>
    /// The arc passes through this line vertically.
    /// </summary>
    Middle
}
