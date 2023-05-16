namespace Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// Represents a breakpoint that triggers when the CPU's execution address is within a specified address range.
/// </summary>
public class AddressRangeBreakPoint : BreakPoint {
    /// <summary>
    /// The start address of the range.
    /// </summary>
    public long StartAddress { get; private set; }

    /// <summary>
    /// The end address of the range.
    /// </summary>
    public long EndAddress { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AddressRangeBreakPoint"/> class.
    /// </summary>
    /// <param name="breakPointType">The type of the breakpoint.</param>
    /// <param name="startAddress">The start address of the range.</param>
    /// <param name="endAddress">The end address of the range.</param>
    /// <param name="onReached">The action to execute when the breakpoint is reached.</param>
    /// <param name="isRemovedOnTrigger">A value indicating whether the breakpoint should be removed after it's triggered.</param>
    public AddressRangeBreakPoint(BreakPointType breakPointType, long startAddress, long endAddress, Action<BreakPoint> onReached, bool isRemovedOnTrigger) : base(breakPointType, onReached, isRemovedOnTrigger) {
        StartAddress = startAddress;
        EndAddress = endAddress;
    }

    /// <summary>
    /// Determines whether the breakpoint matches a given address.
    /// </summary>
    /// <param name="address">The address to check.</param>
    /// <returns><c>true</c> if the address is within the range of the breakpoint, otherwise <c>false</c>.</returns>
    public override bool Matches(long address) {
        return StartAddress <= address && EndAddress >= address;
    }

    /// <summary>
    /// Determines whether the breakpoint matches a given address range.
    /// </summary>
    /// <param name="startAddress">The start address of the range to check.</param>
    /// <param name="endAddress">The end address of the range to check.</param>
    /// <returns><c>true</c> if the address range overlaps with the range of the breakpoint, otherwise <c>false</c>.</returns>
    public override bool Matches(long startAddress, long endAddress) {
        return startAddress <= EndAddress && endAddress >= StartAddress;
    }
}
