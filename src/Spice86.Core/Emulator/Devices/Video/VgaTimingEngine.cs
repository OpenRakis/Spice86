namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

/// <summary>
///     Drives VGA timing signals (vertical retrace, display-disabled) via the
///     <see cref="DeviceScheduler"/> so that they fire deterministically
///     on the emulation thread instead of on a UI timer.
///     Each scanline event chains to the next, matching the hardware beam progression.
/// </summary>
public class VgaTimingEngine {
    private const double BlinkPeriodMs = 500.0;

    private readonly IVideoState _state;
    private readonly DeviceScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly Renderer _renderer;
    private readonly VgaBlinkState _blinkState;

    private VgaTimingParameters _timing;
    private int _visibleScanlines;
    private double _lastFrameStartMs;
    private double _activeDisplayMs;
    private double _hblankDurationMs;

    /// <summary>
    ///     The emulated duration of the last completed frame (active display period).
    /// </summary>
    public TimeSpan LastFrameDuration { get; private set; }

    /// <summary>
    ///     Creates a new VGA timing engine and schedules the initial frame and blink events.
    /// </summary>
    /// <param name="state">The video state holding CRT controller registers.</param>
    /// <param name="scheduler">The emulation loop scheduler for deterministic event scheduling.</param>
    /// <param name="clock">The emulated clock providing the master time source.</param>
    /// <param name="renderer">The renderer to invoke for per-scanline pixel conversion.</param>
    /// <param name="blinkState">Shared blink state toggled by this engine for text-mode blinking.</param>
    public VgaTimingEngine(IVideoState state, DeviceScheduler scheduler,
        IEmulatedClock clock, Renderer renderer, VgaBlinkState blinkState) {
        _state = state;
        _scheduler = scheduler;
        _clock = clock;
        _renderer = renderer;
        _blinkState = blinkState;
        _timing = state.BeamTiming.Refresh(0);
        _scheduler.AddEvent(OnFrameStart, 0);
        _scheduler.AddEvent(OnBlinkToggle, BlinkPeriodMs);
    }

    private void OnFrameStart(uint value) {
        if (RestartIfTimingChanged()) {
            return;
        }
        _lastFrameStartMs = _scheduler.EventTimeMs;
        _timing = _state.BeamTiming.StartFrame(_lastFrameStartMs);
        _visibleScanlines = Math.Min(_timing.VisibleScanlines, _timing.TotalScanlines);
        _activeDisplayMs = _timing.HorizontalDisplayEndMs;
        _hblankDurationMs = _timing.LineDurationMs - _activeDisplayMs;
        UpdateBeamStatus();
        _renderer.BeginFrame();
        _scheduler.AddEvent(OnDisplayEnd, _visibleScanlines * _timing.LineDurationMs);
        _scheduler.AddEvent(OnFrameStart, _timing.FrameDurationMs);
        if (_timing.VerticalRetraceStartMs < _timing.FrameDurationMs) {
            if (_timing.VerticalRetraceStartMs > 0) {
                _scheduler.AddEvent(OnBeamStatusChange, _timing.VerticalRetraceStartMs);
            }
            double retraceEndMs = _timing.VerticalRetraceEndMs % _timing.FrameDurationMs;
            if (retraceEndMs > 0) {
                _scheduler.AddEvent(OnBeamStatusChange, retraceEndMs);
            }
        }
        OnScanlineActive(0);
    }

    private void OnScanlineActive(uint scanline) {
        if (RestartIfTimingChanged()) {
            return;
        }
        UpdateBeamStatus();
        _renderer.RenderScanline();
        _scheduler.AddEvent(OnScanlineHBlank, _activeDisplayMs, scanline);
    }

    private void OnScanlineHBlank(uint scanline) {
        if (RestartIfTimingChanged()) {
            return;
        }
        UpdateBeamStatus();
        if (scanline + 1 < (uint)_visibleScanlines) {
            if (_hblankDurationMs <= 0) {
                OnScanlineActive(scanline + 1);
            } else {
                _scheduler.AddEvent(OnScanlineActive, _hblankDurationMs, scanline + 1);
            }
        }
    }

    private void OnDisplayEnd(uint value) {
        if (RestartIfTimingChanged()) {
            return;
        }
        UpdateBeamStatus();
        LastFrameDuration = TimeSpan.FromMilliseconds(_scheduler.EventTimeMs - _lastFrameStartMs);
        _renderer.CompleteFrame();
    }

    private void OnBeamStatusChange(uint value) {
        if (!RestartIfTimingChanged()) {
            UpdateBeamStatus();
        }
    }

    private void UpdateBeamStatus() {
        _state.GeneralRegisters.InputStatusRegister1.Value = _state.BeamTiming.ReadStatus(_scheduler.EventTimeMs);
    }

    private bool RestartIfTimingChanged() {
        VgaTimingParameters timing = _state.BeamTiming.Refresh(_clock.ElapsedTimeMs);
        if (ReferenceEquals(timing, _timing)) {
            return false;
        }
        _scheduler.RemoveEvents(OnFrameStart);
        _scheduler.RemoveEvents(OnScanlineActive);
        _scheduler.RemoveEvents(OnScanlineHBlank);
        _scheduler.RemoveEvents(OnDisplayEnd);
        _scheduler.RemoveEvents(OnBeamStatusChange);
        _timing = timing;
        _state.IsRenderingDirty = true;
        _scheduler.AddEvent(OnFrameStart, 0);
        return true;
    }

    private void OnBlinkToggle(uint value) {
        _blinkState.IsBlinkPhaseHigh = !_blinkState.IsBlinkPhaseHigh;
        _blinkState.MarkChanged();
        _scheduler.AddEvent(OnBlinkToggle, BlinkPeriodMs);
    }
}
