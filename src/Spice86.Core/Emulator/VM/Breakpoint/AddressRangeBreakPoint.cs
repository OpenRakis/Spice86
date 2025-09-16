namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Shared.Emulator.VM.Breakpoint;

using System;

/// <summary>
/// Represents a breakpoint that is triggered when the CPU's execution address is within a specified range.
/// </summary>
public class AddressRangeBreakPoint : AddressBreakPoint {
    /// <summary>
    /// The start of the address range for the breakpoint.
    /// </summary>
    public long StartAddress { get; }

    /// <summary>
    /// The end of the address range for the breakpoint.
    /// </summary>
    public long EndAddress { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AddressRangeBreakPoint"/> class.
    /// </summary>
    /// <param name="breakPointType">The type of the breakpoint.</param>
    /// <param name="startAddress">The start of the address range for the breakpoint.</param>
    /// <param name="endAddress">The end of the address range for the breakpoint.</param>
    /// <param name="onReached">The action to take when the breakpoint is reached.</param>
    /// <param name="isRemovedOnTrigger">A value indicating whether the breakpoint should be removed after it is triggered.</param>
    public AddressRangeBreakPoint(BreakPointType breakPointType, long startAddress,
        long endAddress, Action<BreakPoint> onReached, bool isRemovedOnTrigger)
        : base(breakPointType, startAddress, onReached, isRemovedOnTrigger) {
        StartAddress = startAddress;
        EndAddress = endAddress;
    }

    /// <inheritdoc/>
    public override bool Matches(long address) {
        return address >= StartAddress && address <= EndAddress;
    }
}