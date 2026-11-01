namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// A breakpoint registered at many addresses at once through
/// <see cref="BreakPointHolder.ToggleBreakPointAt"/>: the dictionary key selects the address, so
/// matching is only the enabled check of the base class. The triggering action receives the
/// accessed address. Internal watchers such as the changed-bytes map use it; it is never
/// serialized or shown in the UI (it is not an <see cref="AddressBreakPoint"/> and not a user
/// breakpoint).
/// </summary>
public sealed class MultiAddressBreakPoint : BreakPoint {
    /// <summary>
    /// Creates a breakpoint that can be registered at any number of addresses.
    /// </summary>
    /// <param name="breakPointType">The type of breakpoint to create.</param>
    /// <param name="onReached">The action to execute with the accessed address when the
    /// breakpoint is triggered.</param>
    public MultiAddressBreakPoint(BreakPointType breakPointType,
        Action<BreakPoint, long> onReached)
        : base(breakPointType, onReached, false) {
    }
}
