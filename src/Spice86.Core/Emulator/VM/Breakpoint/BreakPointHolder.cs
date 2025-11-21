namespace Spice86.Core.Emulator.VM.Breakpoint;

using System.Linq;

/// <summary>
/// Holds breakpoints and triggers them when certain conditions are met.
/// </summary>
public class BreakPointHolder {
    private readonly Dictionary<long, List<BreakPoint>> _addressBreakPoints = new(1000);
    private readonly List<BreakPoint> _unconditionalBreakPoints = new(1000);
    private readonly HashSet<BreakPoint> _registeredBreakPoints = [];
    private int _activeBreakpoints;

    /// <summary>
    /// Gets a value indicating whether at least one breakpoint is currently enabled.
    /// </summary>
    public bool HasActiveBreakpoints => _activeBreakpoints > 0;

    private IEnumerable<BreakPoint> GetAllBreakpoints() {
        return _addressBreakPoints
            .OrderBy(kvp => kvp.Key)
            .SelectMany(kvp => kvp.Value)
            .Concat(_unconditionalBreakPoints);
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
                RegisterBreakPoint(breakPoint);
                return;
            }

            if (breakPointList.Contains(breakPoint)) {
                return;
            }

            breakPointList.Add(breakPoint);
            RegisterBreakPoint(breakPoint);
        } else if (breakPointList != null && breakPointList.Remove(breakPoint)) {
            if (breakPointList.Count == 0) {
                _addressBreakPoints.Remove(address);
            }

            UnregisterBreakPoint(breakPoint);
        }
    }

    private void ToggleUnconditionalBreakPoint(BreakPoint breakPoint, bool on) {
        if (on) {
            if (_unconditionalBreakPoints.Contains(breakPoint)) {
                return;
            }

            _unconditionalBreakPoints.Add(breakPoint);
            RegisterBreakPoint(breakPoint);
        } else if (_unconditionalBreakPoints.Remove(breakPoint)) {
            UnregisterBreakPoint(breakPoint);
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
                UnregisterBreakPoint(breakPoint);
                i--;
            }
        }

        return triggered;
    }

    private void RegisterBreakPoint(BreakPoint breakPoint) {
        if (!_registeredBreakPoints.Add(breakPoint)) {
            return;
        }

        breakPoint.IsEnabledChanged += OnBreakPointIsEnabledChanged;
        if (breakPoint.IsEnabled) {
            _activeBreakpoints++;
        }
    }

    private void UnregisterBreakPoint(BreakPoint breakPoint) {
        if (!_registeredBreakPoints.Remove(breakPoint)) {
            return;
        }

        breakPoint.IsEnabledChanged -= OnBreakPointIsEnabledChanged;
        if (breakPoint.IsEnabled && _activeBreakpoints > 0) {
            _activeBreakpoints--;
        }
    }

    private void OnBreakPointIsEnabledChanged(BreakPoint breakPoint, bool isEnabled) {
        if (isEnabled) {
            _activeBreakpoints++;
        } else if (_activeBreakpoints > 0) {
            _activeBreakpoints--;
        }
    }
}
