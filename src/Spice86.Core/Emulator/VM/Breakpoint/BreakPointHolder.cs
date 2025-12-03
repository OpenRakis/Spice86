namespace Spice86.Core.Emulator.VM.Breakpoint;

using System.Linq;

/// <summary>
/// Holds breakpoints and triggers them when certain conditions are met.
/// </summary>
public class BreakPointHolder {
    private readonly Dictionary<long, List<BreakPoint>> _addressBreakPoints = new(1000);
    private readonly List<BreakPoint> _unconditionalBreakPoints = new(1000);
    private readonly HashSet<BreakPoint> _registeredBreakPoints = [];

    /// <summary>
    /// Gets a value indicating whether this BreakPointHolder is empty.
    /// </summary>
    public bool IsEmpty => _addressBreakPoints.Count == 0 && _unconditionalBreakPoints.Count == 0;

    /// <summary>
    /// Gets a value indicating whether at least one breakpoint is currently enabled.
    /// This iterates through all registered breakpoints to check their enabled state.
    /// </summary>
    public bool HasActiveBreakpoints {
        get {
            foreach (BreakPoint breakPoint in _registeredBreakPoints) {
                if (breakPoint.IsEnabled) {
                    return true;
                }
            }
            return false;
        }
    }

    private IEnumerable<BreakPoint> GetAllBreakpoints() {
        return _addressBreakPoints.Values
        .SelectMany(list => list).Concat(_unconditionalBreakPoints);
    }

    internal IEnumerable<AddressBreakPoint> SerializableBreakpoints => GetAllBreakpoints().Where
        (x => x.IsUserBreakpoint).OfType<AddressBreakPoint>();

    /// <summary>
    /// Toggles the specified breakpoint on or off.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to toggle.</param>
    /// <param name="on">True to enable the breakpoint; false to disable it.</param>
    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        switch (breakPoint) {
            case UnconditionalBreakPoint:
                ToggleUnconditionalBreakPoint(breakPoint, on);
                break;
            case AddressBreakPoint addressBreakPoint:
                ToggleAddressBreakPoint(addressBreakPoint, on);
                break;
        }
    }

    private void ToggleAddressBreakPoint(AddressBreakPoint breakPoint, bool on) {
        long address = breakPoint.Address;
        _addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList);
        if (on) {
            if (breakPointList == null) {
                _addressBreakPoints.Add(address, [breakPoint]);
                _registeredBreakPoints.Add(breakPoint);
                return;
            }

            if (_registeredBreakPoints.Contains(breakPoint)) {
                return;
            }

            breakPointList.Add(breakPoint);
            _registeredBreakPoints.Add(breakPoint);
        } else if (breakPointList != null && breakPointList.Remove(breakPoint)) {
            if (breakPointList.Count == 0) {
                _addressBreakPoints.Remove(address);
            }

            _registeredBreakPoints.Remove(breakPoint);
        }
    }

    private void ToggleUnconditionalBreakPoint(BreakPoint breakPoint, bool on) {
        if (on) {
            if (_registeredBreakPoints.Contains(breakPoint)) {
                return;
            }

            _unconditionalBreakPoints.Add(breakPoint);
            _registeredBreakPoints.Add(breakPoint);
        } else if (_unconditionalBreakPoints.Remove(breakPoint)) {
            _registeredBreakPoints.Remove(breakPoint);
        }
    }

    /// <summary>
    /// Triggers all breakpoints that match the specified address.
    /// </summary>
    /// <param name="address">The address to match.</param>
    /// <returns>true if trigged, false instead</returns>
    public bool TriggerMatchingBreakPoints(long address) {
        bool triggered = false;
        if (_addressBreakPoints.Count > 0) {
            if (_addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                triggered = TriggerBreakPointsFromList(breakPointList, address);
                if (breakPointList.Count == 0) {
                    _addressBreakPoints.Remove(address);
                }
            }
        }

        if (_unconditionalBreakPoints.Count > 0) {
            triggered |= TriggerBreakPointsFromList(_unconditionalBreakPoints, address);
        }

        return triggered;
    }

    private bool TriggerBreakPointsFromList(List<BreakPoint> breakPointList, long address) {
        bool triggered = false;
        for (int i = 0; i < breakPointList.Count; i++) {
            BreakPoint breakPoint = breakPointList[i];
            if (!breakPoint.Matches(address)) {
                continue;
            }

            breakPoint.Trigger();
            triggered = true;

            if (breakPoint.IsRemovedOnTrigger) {
                breakPointList.RemoveAt(i);
                _registeredBreakPoints.Remove(breakPoint);
                i--;
            }
        }

        return triggered;
    }
}