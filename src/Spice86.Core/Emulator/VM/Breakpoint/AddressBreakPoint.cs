namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// Represents a breakpoint triggered when the program reaches a specific memory address.
/// </summary>
public class AddressBreakPoint : BreakPoint {
    /// <summary>
    /// The memory address the breakpoint is triggered on.
    /// </summary>
    public long Address { get; private set; }

    private Func<long, bool>? _additionalTriggerCondition;

    /// <summary>
    /// The condition expression string for conditional breakpoints.
    /// </summary>
    public string? ConditionExpression { get; private set; }

    /// <summary>
    /// Creates a new address breakpoint instance.
    /// </summary>
    /// <param name="breakPointType">The type of breakpoint to create.</param>
    /// <param name="address">The memory address the breakpoint is triggered on.</param>
    /// <param name="onReached">The action to execute when the breakpoint is triggered.</param>
    /// <param name="isRemovedOnTrigger">A value indicating whether the breakpoint is removed when triggered.</param>
    /// <param name="additionalTriggerCondition">Additional condition for triggering. Not used if null.</param>
    /// <param name="conditionExpression">The condition expression string for serialization purposes.</param>
    public AddressBreakPoint(BreakPointType breakPointType, long address,
        Action<BreakPoint> onReached, bool isRemovedOnTrigger, Func<long, bool>? additionalTriggerCondition = null,
        string? conditionExpression = null)
        : base(breakPointType, onReached, isRemovedOnTrigger) {
        Address = address;
        _additionalTriggerCondition = additionalTriggerCondition;
        ConditionExpression = conditionExpression;
    }

    /// <summary>
    /// Determines whether the breakpoint matches the specified memory address.
    /// </summary>
    /// <param name="address">The memory address to match against the breakpoint.</param>
    /// <returns>True if the breakpoint matches the address, otherwise false.</returns>
    public override bool Matches(long address) {
        if (!base.Matches(address)) {
            return false;
        }
        if (_additionalTriggerCondition != null) {
            if (!_additionalTriggerCondition.Invoke(address)) {
                return false;
            }
        }
        return Address == address;
    }
}