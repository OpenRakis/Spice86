namespace Spice86.Core.Emulator.VM.Breakpoint;

using System.Linq;

/// <summary>
/// Holds breakpoints and triggers them when certain conditions are met.
/// </summary>
public class BreakPointHolder {
    private readonly Dictionary<long, List<BreakPoint>> _addressBreakPoints = new(1000);

    /// <summary>
    /// Bitmap filter over <see cref="_addressBreakPoints"/> keys: the bit of every covered
    /// address is set exactly when the dictionary contains that key. Addresses outside the
    /// covered range always take the dictionary path.
    /// </summary>
    private readonly AddressBitmap _addressFilter = new();
    private readonly List<BreakPoint> _unconditionalBreakPoints = new(1000);
    private readonly HashSet<BreakPoint> _registeredBreakPoints = [];

    /// <summary>
    /// How many addresses each breakpoint object is registered at: register and unregister
    /// happen once per object, on its first and last address respectively.
    /// </summary>
    private readonly Dictionary<BreakPoint, int> _addressRegistrationCounts = new();
    private int _activeBreakpoints;

    /// <summary>
    /// Gets a value indicating whether this BreakPointHolder is empty.
    /// </summary>
    public bool IsEmpty => _addressBreakPoints.Count == 0 && _unconditionalBreakPoints.Count == 0;

    /// <summary>
    /// Gets a value indicating whether at least one breakpoint is currently enabled.
    /// </summary>
    public bool HasActiveBreakpoints => _activeBreakpoints > 0;

    private IEnumerable<BreakPoint> GetAllBreakpoints() {
        return _addressBreakPoints.Values
        .SelectMany(list => list).Concat(_unconditionalBreakPoints);
    }

    internal IEnumerable<AddressBreakPoint> SerializableBreakpoints => GetAllBreakpoints().Where
        (x => x.IsUserBreakpoint).OfType<AddressBreakPoint>();

    internal IEnumerable<UnconditionalBreakPoint> SerializableWildcardBreakpoints =>
        _unconditionalBreakPoints.Where(x => x.IsUserBreakpoint).OfType<UnconditionalBreakPoint>();

    /// <summary>
    /// Toggles the specified breakpoint on or off.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to toggle.</param>
    /// <param name="on">True to enable the breakpoint; false to disable it.</param>
    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        switch (breakPoint) {
            case MultiAddressBreakPoint:
                throw new InvalidOperationException(
                    "A multi-address breakpoint has no address of its own; use ToggleBreakPointAt.");
            case UnconditionalBreakPoint:
                ToggleUnconditionalBreakPoint(breakPoint, on);
                break;
            case AddressBreakPoint addressBreakPoint:
                ToggleAddressBreakPoint(addressBreakPoint, on);
                break;
        }
    }

    private void ToggleAddressBreakPoint(AddressBreakPoint breakPoint, bool on) =>
        ToggleBreakPointAt(breakPoint, breakPoint.Address, on);

    /// <summary>
    /// Toggles the specified breakpoint on or off at the specified address. One breakpoint
    /// object can be registered at many addresses: it is registered with the holder on its
    /// first address and unregistered when it leaves its last address.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to toggle.</param>
    /// <param name="address">The address to register it at or remove it from.</param>
    /// <param name="on">True to enable the breakpoint at this address; false to disable it.</param>
    public void ToggleBreakPointAt(BreakPoint breakPoint, long address, bool on) {
        _addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList);
        if (on) {
            if (breakPointList == null) {
                _addressBreakPoints.Add(address, [breakPoint]);
                _addressFilter.Set(address);
                RegisterBreakPointAt(breakPoint);
                return;
            }

            if (breakPointList.Contains(breakPoint)) {
                return;
            }

            breakPointList.Add(breakPoint);
            RegisterBreakPointAt(breakPoint);
        } else if (breakPointList != null && breakPointList.Remove(breakPoint)) {
            if (breakPointList.Count == 0) {
                _addressBreakPoints.Remove(address);
                _addressFilter.Clear(address);
            }

            UnregisterBreakPointAt(breakPoint);
        }
    }

    private void ToggleUnconditionalBreakPoint(BreakPoint breakPoint, bool on) {
        if (on) {
            if (_registeredBreakPoints.Contains(breakPoint)) {
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
        if (_addressBreakPoints.Count > 0 && _addressFilter.Contains(address)) {
            if (_addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                triggered = TriggerBreakPointsFromList(breakPointList, address);
                if (breakPointList.Count == 0) {
                    _addressBreakPoints.Remove(address);
                    _addressFilter.Clear(address);
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
        for (int i = breakPointList.Count - 1; i >= 0; i--) {
            BreakPoint breakPoint = breakPointList[i];
            if (!breakPoint.Matches(address)) {
                continue;
            }
            if (breakPoint.IsRemovedOnTrigger) {
                breakPointList.RemoveAt(i);
                UnregisterBreakPointAt(breakPoint);
            }
            // trigger it later because action might try to delete it
            breakPoint.Trigger(address);
            triggered = true;

        }

        return triggered;
    }

    private void RegisterBreakPointAt(BreakPoint breakPoint) {
        _addressRegistrationCounts.TryGetValue(breakPoint, out int count);
        _addressRegistrationCounts[breakPoint] = count + 1;
        if (count == 0) {
            RegisterBreakPoint(breakPoint);
        }
    }

    private void UnregisterBreakPointAt(BreakPoint breakPoint) {
        if (!_addressRegistrationCounts.TryGetValue(breakPoint, out int count)) {
            UnregisterBreakPoint(breakPoint);
            return;
        }

        count--;
        if (count == 0) {
            _addressRegistrationCounts.Remove(breakPoint);
            UnregisterBreakPoint(breakPoint);
            return;
        }

        _addressRegistrationCounts[breakPoint] = count;
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
        if (breakPoint.IsEnabled) {
            DecrementActiveBreakpoints();
        }
    }

    private void OnBreakPointIsEnabledChanged(BreakPoint breakPoint, bool isEnabled) {
        if (isEnabled) {
            _activeBreakpoints++;
        } else {
            DecrementActiveBreakpoints();
        }
    }

    private void DecrementActiveBreakpoints() {
        _activeBreakpoints--;
        // This should never happen, but as a safeguard, throw if the count becomes negative.
        if (_activeBreakpoints < 0) {
            throw new InvalidOperationException("Active breakpoints count cannot be negative.");
        }
    }
}
