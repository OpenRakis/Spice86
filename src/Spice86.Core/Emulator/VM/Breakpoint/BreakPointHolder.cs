namespace Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// Holds breakpoints and triggers them when certain conditions are met.
/// </summary>
public class BreakPointHolder {
    private readonly Dictionary<long, List<BreakPoint>> _addressBreakPoints = new();

    private readonly List<BreakPoint> _unconditionalBreakPoints = new();
    private readonly List<BreakPoint> _rangeBreakPoints = new();

    /// <summary>
    /// Gets a value indicating whether this BreakPointHolder is empty.
    /// </summary>
    public bool IsEmpty => _addressBreakPoints.Count == 0 && _unconditionalBreakPoints.Count == 0;

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
            case AddressRangeBreakPoint addressRangeBreakPoint:
                ToggleRangeBreakPoint(addressRangeBreakPoint, on);
                break;
        }
    }

    
    /// <summary>
    /// Triggers all breakpoints that match the specified address range.
    /// </summary>
    /// <param name="startAddress">The start address of the range.</param>
    /// <param name="endAddress">The end address of the range.</param>
    public void TriggerBreakPointsWithAddressRange(long startAddress, long endAddress) {
        if (_addressBreakPoints.Count > 0) {
            foreach (List<BreakPoint> breakPointList in _addressBreakPoints.Values) {
                TriggerBreakPointsWithAddressRangeFromList(breakPointList, startAddress, endAddress);
            }
        }

        if (_unconditionalBreakPoints.Count > 0) {
            TriggerBreakPointsWithAddressRangeFromList(_unconditionalBreakPoints, startAddress, endAddress);
        }
        if (_rangeBreakPoints.Count > 0) {
            TriggerBreakPointsWithAddressRangeFromList(_rangeBreakPoints, startAddress, endAddress);
        }
    }
    
    /// <summary>
    /// Triggers all breakpoints that match the specified address.
    /// </summary>
    /// <param name="address">The address to match.</param>
    public void TriggerMatchingBreakPoints(long address) {
        if (_addressBreakPoints.Count > 0) {
            if (_addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                TriggerBreakPointsFromList(breakPointList, address);
                if (breakPointList.Count == 0) {
                    _addressBreakPoints.Remove(address);
                }
            }
        }

        if (_unconditionalBreakPoints.Count > 0) {
            TriggerBreakPointsFromList(_unconditionalBreakPoints, address);
        }
        if (_rangeBreakPoints.Count > 0) {
            TriggerBreakPointsFromList(_rangeBreakPoints, address);
        }
    }

    private static void TriggerBreakPointsFromList(List<BreakPoint> breakPointList, long address) {
        for (int i = 0; i < breakPointList.Count; i++) {
            BreakPoint breakPoint = breakPointList[i];
            if (breakPoint.Matches(address)) {
                breakPoint.Trigger();
                if (breakPoint.IsRemovedOnTrigger) {
                    breakPointList.Remove(breakPoint);
                }
            }
        }
    }

    private static void TriggerBreakPointsWithAddressRangeFromList(List<BreakPoint> breakPointList, long startAddress, long endAddress) {
        for (int i = 0; i < breakPointList.Count; i++) {
            BreakPoint breakPoint = breakPointList[i];
            if (breakPoint.Matches(startAddress, endAddress)) {
                breakPoint.Trigger();
                if (breakPoint.IsRemovedOnTrigger) {
                    breakPointList.Remove(breakPoint);
                }
            }
        }
    }

    private void ToggleAddressBreakPoint(AddressBreakPoint breakPoint, bool on) {
        long address = breakPoint.Address;
        if (on) {
            if (!_addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                _addressBreakPoints.Add(address, new List<BreakPoint>() { breakPoint });
            } else {
                breakPointList.Add(breakPoint);
            }
        } else {
            if (_addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                breakPointList.Remove(breakPoint);
                if (breakPointList.Count == 0) {
                    _addressBreakPoints.Remove(address);
                }
            }
        }
    }

    private void ToggleRangeBreakPoint(AddressRangeBreakPoint breakPoint, bool on) {
        if (on) {
            _rangeBreakPoints.Add(breakPoint);
        } else {
            _rangeBreakPoints.Remove(breakPoint);
        }
    }

    private void ToggleUnconditionalBreakPoint(BreakPoint breakPoint, bool on) {
        if (on) {
            _unconditionalBreakPoints.Add(breakPoint);
        } else {
            _unconditionalBreakPoints.Remove(breakPoint);
        }
    }
}