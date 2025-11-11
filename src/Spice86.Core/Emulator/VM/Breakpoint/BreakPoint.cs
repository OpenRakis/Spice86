namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Shared.Emulator.VM.Breakpoint;

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

    private bool _isEnabled = true;

    /// <summary>
    /// Gets or sets a value indicating whether this breakpoint can be matched and triggered.
    /// Setting this property raises <see cref="IsEnabledChanged"/> when the value changes.
    /// </summary>
    public bool IsEnabled {
        get => _isEnabled;
        set {
            if (_isEnabled == value) {
                return;
            }

            _isEnabled = value;
            IsEnabledChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Occurs when <see cref="IsEnabled"/> changes.
    /// </summary>
    internal event Action<BreakPoint, bool>? IsEnabledChanged;

    /// <summary>
    /// The action to take when the breakpoint is reached.
    /// </summary>
    public Action<BreakPoint> OnReached { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether this breakpoint must be saved/restored to/from a file.
    /// </summary>
    public bool IsUserBreakpoint { get; set; }

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
