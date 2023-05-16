namespace Spice86.Core.Emulator.VM.Breakpoint;

using System;

/// <summary>
/// A breakpoint that is triggered unconditionally when reached.
/// </summary>
public class UnconditionalBreakPoint : BreakPoint {
    /// <summary>
    /// Initializes a new instance of the <see cref="UnconditionalBreakPoint"/> class.
    /// </summary>
    /// <param name="breakPointType">The type of the breakpoint.</param>
    /// <param name="onReached">The action to perform when the breakpoint is reached.</param>
    /// <param name="removeOnTrigger">A value indicating whether to remove the breakpoint after it has been triggered.</param>
    public UnconditionalBreakPoint(BreakPointType breakPointType, Action<BreakPoint> onReached, bool removeOnTrigger) : base(breakPointType, onReached, removeOnTrigger) {
    }

    /// <summary>
    /// Determines whether the breakpoint matches the specified address.
    /// </summary>
    /// <param name="address">The address to check against the breakpoint.</param>
    /// <returns>Always returns <c>true</c>.</returns>
    public override bool Matches(long address) {
        return true;
    }

    /// <summary>
    /// Determines whether the breakpoint matches the specified address range.
    /// </summary>
    /// <param name="startAddress">The starting address of the range to check against the breakpoint.</param>
    /// <param name="endAddress">The ending address of the range to check against the breakpoint.</param>
    /// <returns>Always returns <c>true</c>.</returns>
    public override bool Matches(long startAddress, long endAddress) {
        return true;
    }
}
