﻿namespace Spice86.ViewModels;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using System;

public partial class BreakpointRangeViewModel : BreakpointViewModel {
    public BreakpointRangeViewModel(BreakpointsViewModel breakpointsViewModel,
        EmulatorBreakpointsManager emulatorBreakpointsManager, long trigger, long endTrigger,
        BreakPointType type, bool isRemovedOnTrigger, Action onReached,
        string comment = "")
        : base(breakpointsViewModel, emulatorBreakpointsManager, trigger, type,
            isRemovedOnTrigger, onReached, comment) {
        EndTrigger = endTrigger;
    }

    public long EndTrigger { get; }

    public override void Disable() {
        if(!IsEnabled) {
            return;
        }
        for(long i = Address; i < EndTrigger; i++) {
            DisableInternal(CreateBreakpointWithAddress(i));
        }
    }

    public override void Enable() {
        if(IsEnabled) {
            return;
        }
        for (long i = Address; i < EndTrigger; i++) {
            EnableInternal(CreateBreakpointWithAddress(i));
        }
    }
}
