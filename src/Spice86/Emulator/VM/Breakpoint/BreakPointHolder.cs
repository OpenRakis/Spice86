namespace Spice86.Emulator.VM.Breakpoint;

using System.Collections.Generic;

public class BreakPointHolder {
    private readonly Dictionary<long, List<BreakPoint>> _breakPoints = new();

    private readonly List<BreakPoint> _unconditionalBreakPoints = new();

    public bool IsEmpty => _breakPoints.Count == 0 && _unconditionalBreakPoints.Count == 0;

    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        if (breakPoint is UnconditionalBreakPoint) {
            ToggleUnconditionalBreakPointBreakPoint(breakPoint, on);
        } else {
            ToggleConditionalBreakPoint(breakPoint, on);
        }
    }

    public void TriggerBreakPointsWithAddressRange(long startAddress, long endAddress) {
        if (_breakPoints.Count > 0) {
            foreach (List<BreakPoint> breakPointList in _breakPoints.Values) {
                TriggerBreakPointsWithAddressRangeFromList(breakPointList, startAddress, endAddress);
            }
        }

        if (_unconditionalBreakPoints.Count > 0) {
            TriggerBreakPointsWithAddressRangeFromList(_unconditionalBreakPoints, startAddress, endAddress);
        }
    }

    public void TriggerMatchingBreakPoints(long address) {
        if (_breakPoints.Count > 0) {
            if (_breakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                TriggerBreakPointsFromList(breakPointList, address);
                if (breakPointList.Count == 0) {
                    _breakPoints.Remove(address);
                }
            }
        }

        if (_unconditionalBreakPoints.Count > 0) {
            TriggerBreakPointsFromList(_unconditionalBreakPoints, address);
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

    private void ToggleConditionalBreakPoint(BreakPoint breakPoint, bool on) {
        long address = breakPoint.Address;
        if (on) {
            List<BreakPoint> breakPointList = _breakPoints.ComputeIfAbsent(address, new());
            breakPointList.Add(breakPoint);
        } else {
            if (_breakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                breakPointList.Remove(breakPoint);
                if (breakPointList.Count == 0) {
                    _breakPoints.Remove(address);
                }
            }
        }
    }

    private void ToggleUnconditionalBreakPointBreakPoint(BreakPoint breakPoint, bool on) {
        if (on) {
            _unconditionalBreakPoints.Add(breakPoint);
        } else {
            _unconditionalBreakPoints.Remove(breakPoint);
        }
    }
}
