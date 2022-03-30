namespace Spice86.Emulator.VM.Breakpoint;

using System.Collections.Generic;

public class BreakPointHolder {
    private readonly Dictionary<long, List<BreakPoint>> _addressBreakPoints = new();

    private readonly List<BreakPoint> _unconditionalBreakPoints = new();
    private readonly List<BreakPoint> _rangeBreakPoints = new();


    public bool IsEmpty => _addressBreakPoints.Count == 0 && _unconditionalBreakPoints.Count == 0;

    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        if (breakPoint is UnconditionalBreakPoint) {
            ToggleUnconditionalBreakPoint(breakPoint, on);
        } else if (breakPoint is AddressBreakPoint addressBreakPoint) {
            ToggleAddressBreakPoint(addressBreakPoint, on);
        }else if (breakPoint is AddressRangeBreakPoint addressRangeBreakPoint) {
            ToggleRangeBreakPoint(addressRangeBreakPoint, on);
        }
    }

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
            List<BreakPoint> breakPointList = _addressBreakPoints.ComputeIfAbsent(address, new());
            breakPointList.Add(breakPoint);
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