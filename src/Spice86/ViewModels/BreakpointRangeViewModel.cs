namespace Spice86.ViewModels;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using System;

public partial class BreakpointRangeViewModel : BreakpointViewModel {
    private readonly List<BreakPoint> _breakpoints = new List<BreakPoint>();

    public BreakpointRangeViewModel(BreakpointsViewModel breakpointsViewModel,
        EmulatorBreakpointsManager emulatorBreakpointsManager, long trigger, long endTrigger,
        BreakPointType type, bool isRemovedOnTrigger, Action onReached,
        string comment = "")
        : base(breakpointsViewModel, emulatorBreakpointsManager, trigger, type,
            isRemovedOnTrigger, onReached, comment) {
        EndTrigger = endTrigger;
        for (long i = Address; i <= EndTrigger; i++) {
            AddressBreakPoint breakpoint = CreateBreakpointWithAddress(i);
            breakpoint.IsEnabled = true;
            _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, on: breakpoint.IsEnabled);
            _breakpoints.Add(breakpoint);
        }
    }

    public long EndTrigger { get; }

    public override void Disable() {
        if(!IsEnabled) {
            return;
        }
        foreach (BreakPoint breakpoint in _breakpoints) {
            breakpoint.IsEnabled = false;
        }
        OnPropertyChanged(nameof(IsEnabled));
    }

    public override void Enable() {
        if(IsEnabled) {
            return;
        }
        foreach (BreakPoint breakpoint in _breakpoints) {
            breakpoint.IsEnabled = true;
        }
        OnPropertyChanged(nameof(IsEnabled));
    }
}
