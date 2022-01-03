namespace Spice86.Emulator.Machine.Breakpoint
{
    using System.Collections.Generic;
    using System.Linq;

    public class BreakPointHolder
    {
        private readonly List<BreakPoint> _unconditionalBreakPoints = new();
        private readonly Dictionary<long, List<BreakPoint>> _breakPoints = new();
        public virtual bool IsEmpty()
        {
            return _breakPoints.Any() == false && _unconditionalBreakPoints.Any() == false;
        }

        public virtual void ToggleBreakPoint(BreakPoint breakPoint, bool on)
        {
            if (breakPoint is UnconditionalBreakPoint)
            {
                ToggleUnconditionalBreakPointBreakPoint(breakPoint, on);
            }
            else
            {
                ToggleConditionalBreakPoint(breakPoint, on);
            }
        }

        private void ToggleUnconditionalBreakPointBreakPoint(BreakPoint breakPoint, bool on)
        {
            if (on)
            {
                _unconditionalBreakPoints.Add(breakPoint);
            }
            else
            {
                _unconditionalBreakPoints.Remove(breakPoint);
            }
        }

        private void ToggleConditionalBreakPoint(BreakPoint breakPoint, bool on)
        {
            long address = breakPoint.GetAddress();
            if (on)
            {
                if (_breakPoints.TryGetValue(address, out var breakPointList) == false)
                {
                    breakPointList = new List<BreakPoint>();
                    _breakPoints.Add(address, breakPointList);
                }
                else
                {
                    breakPointList.Add(breakPoint);
                }

            }
            else
            {
                if (_breakPoints.TryGetValue(address, out var breakPointList))
                {
                    breakPointList.Remove(breakPoint);
                    if (breakPointList.Any() == false)
                    {
                        _breakPoints.Remove(address);
                    }
                }
            }
        }

        public virtual void TriggerMatchingBreakPoints(long address)
        {
            if (!_breakPoints.Any() == false)
            {
                if (_breakPoints.TryGetValue(address, out var breakPointList))
                {
                    TriggerBreakPointsFromList(breakPointList, address);
                    if (breakPointList.Any() == false)
                    {
                        _breakPoints.Remove(address);
                    }
                }
            }

            if (!_unconditionalBreakPoints.Any() == false)
            {
                TriggerBreakPointsFromList(_unconditionalBreakPoints, address);
            }
        }

        private static void TriggerBreakPointsFromList(List<BreakPoint> breakPointList, long address)
        {
            for (int i = 0; i < breakPointList.Count; i++)
            {
                BreakPoint breakPoint = breakPointList[i];
                if (breakPoint.Matches(address))
                {
                    breakPoint.Trigger();
                    if (breakPoint.IsRemoveOnTrigger())
                    {
                        breakPointList.Remove(breakPoint);
                    }
                }
            }
        }

        public virtual void TriggerBreakPointsWithAddressRange(long startAddress, long endAddress)
        {
            if (!_breakPoints.Any() == false)
            {
                foreach (List<BreakPoint> breakPointList in _breakPoints.Values)
                {
                    TriggerBreakPointsWithAddressRangeFromList(breakPointList, startAddress, endAddress);
                }
            }

            if (!_unconditionalBreakPoints.Any() == false)
            {
                TriggerBreakPointsWithAddressRangeFromList(_unconditionalBreakPoints, startAddress, endAddress);
            }
        }

        private static void TriggerBreakPointsWithAddressRangeFromList(List<BreakPoint> breakPointList, long startAddress, long endAddress)
        {
            foreach (BreakPoint breakPoint in breakPointList)
            {
                if (breakPoint.Matches(startAddress, endAddress))
                {
                    breakPoint.Trigger();
                }
            }
        }
    }
}