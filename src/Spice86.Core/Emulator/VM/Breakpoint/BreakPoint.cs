namespace Spice86.Core.Emulator.VM.Breakpoint;


/// <summary>
/// Base class for all breakpoints.
/// </summary>
public abstract class BreakPoint {
    /// <summary>
    /// Constructs a new instance of the BreakPoint class.
    /// </summary>
    /// <param name="breakPointType">The type of the breakpoint.</param>
    /// <param name="onReached">The action to take when the breakpoint is reached.</param>
    /// <param name="isRemovedOnTrigger">True if the breakpoint should be removed after being triggered, false otherwise.</param>
    public BreakPoint(BreakPointType breakPointType, Action<BreakPoint> onReached, bool isRemovedOnTrigger) {
        BreakPointType = breakPointType;
        OnReached = onReached;
        IsRemovedOnTrigger = isRemovedOnTrigger;
    }

    /// <summary>
    /// The action to take when the breakpoint is reached.
    /// </summary>
    public Action<BreakPoint> OnReached { get; set; }

    /// <summary>
    /// The type of the breakpoint.
    /// </summary>
    public BreakPointType BreakPointType { get; private set; }

    /// <summary>
    /// True if the breakpoint should be removed after being triggered, false otherwise.
    /// </summary>
    public bool IsRemovedOnTrigger { get; private set; }

    /// <summary>
    /// Determines if the breakpoint matches the specified address.
    /// </summary>
    /// <param name="address">The address to check.</param>
    /// <returns>True if the breakpoint matches the address, false otherwise.</returns>
    public abstract bool Matches(long address);

    /// <summary>
    /// Triggers the breakpoint, calling the OnReached action.
    /// </summary>
    public void Trigger() {
        OnReached.Invoke(this);
    }
}
