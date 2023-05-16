namespace Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// Represents a breakpoint triggered when the program reaches a specific memory address.
/// </summary>
public class AddressBreakPoint : BreakPoint {
    /// <summary>
    /// The memory address the breakpoint is triggered on.
    /// </summary>
    public long Address { get; private set; }

    /// <summary>
    /// Creates a new address breakpoint instance.
    /// </summary>
    /// <param name="breakPointType">The type of breakpoint to create.</param>
    /// <param name="address">The memory address the breakpoint is triggered on.</param>
    /// <param name="onReached">The action to execute when the breakpoint is triggered.</param>
    /// <param name="isRemovedOnTrigger">A value indicating whether the breakpoint is removed when triggered.</param>
    public AddressBreakPoint(BreakPointType breakPointType, long address, Action<BreakPoint> onReached, bool isRemovedOnTrigger) : base(breakPointType, onReached, isRemovedOnTrigger) {
        Address = address;
    }

    /// <summary>
    /// Determines whether the breakpoint matches the specified memory address.
    /// </summary>
    /// <param name="address">The memory address to match against the breakpoint.</param>
    /// <returns>True if the breakpoint matches the address, otherwise false.</returns>
    public override bool Matches(long address) {
        return Address == address;
    }

    /// <summary>
    /// Determines whether the breakpoint matches any memory addresses in the specified range.
    /// </summary>
    /// <param name="startAddress">The start of the memory range to match against the breakpoint.</param>
    /// <param name="endAddress">The end of the memory range to match against the breakpoint.</param>
    /// <returns>True if the breakpoint matches any address in the range, otherwise false.</returns>
    public override bool Matches(long startAddress, long endAddress) {
        return Address >= startAddress && Address < endAddress;
    }
}