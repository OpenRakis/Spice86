// Native Tandy resize scheduling adapted from DOSBox Staging.
// SPDX-FileCopyrightText: 2020-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

/// <summary>Schedules native Tandy drawing setup on the video scheduler thread.</summary>
public sealed class TandyTimingEngine : VgaTimingEngine {
    private readonly TandyVideoState _tandy;
    private readonly DeviceScheduler _scheduler;
    private readonly IEmulatedClock _clock;

    /// <summary>Creates the timing engine and takes ownership of native resize requests.</summary>
    public TandyTimingEngine(IVideoState state, TandyVideoState tandy, DeviceScheduler scheduler,
        IEmulatedClock clock, Renderer renderer, VgaBlinkState blinkState)
        : base(CommitInitialScanout(state, tandy), scheduler, clock, renderer, blinkState) {
        _tandy = tandy;
        _scheduler = scheduler;
        _clock = clock;
        tandy.AttachTimingEngine(this);
    }

    private static IVideoState CommitInitialScanout(IVideoState state, TandyVideoState tandy) {
        tandy.CommitResize();
        return state;
    }

    internal void RequestResize(int delayMs) {
        _scheduler.Dispatch(new ResizeCommand(this, _clock.ElapsedTimeMs + delayMs));
    }

    private void ScheduleResize(double deadlineMs) {
        _scheduler.RemoveEvents(OnResize);
        double delayMs = Math.Max(0, deadlineMs - _scheduler.EventTimeMs);
        if (delayMs == 0) {
            OnResize(0);
        } else {
            _scheduler.AddEvent(OnResize, delayMs);
        }
    }

    private void OnResize(uint value) {
        _scheduler.RemoveEvents(OnResize);
        _tandy.CommitResize();
        RestartFrame();
    }

    private sealed class ResizeCommand : IDeviceSchedulerCommand {
        private readonly TandyTimingEngine _engine;
        private readonly double _deadlineMs;

        public ResizeCommand(TandyTimingEngine engine, double deadlineMs) {
            _engine = engine;
            _deadlineMs = deadlineMs;
        }

        public void Execute() {
            _engine.ScheduleResize(_deadlineMs);
        }
    }
}