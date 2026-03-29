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
    private const double HorizontalLineDurationMs = 1000.0 / 31469.0;

    private readonly IVideoState _state;
    private readonly DeviceScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly Renderer _renderer;
    private readonly VgaBlinkState _blinkState;

    private int _totalScanlines;
    private int _verticalDisplayEndScanline;
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
        _scheduler.AddEvent(OnFrameStart, 0);
        _scheduler.AddEvent(OnBlinkToggle, BlinkPeriodMs);
    }

    private void RecomputeTimingParameters() {
        int totalHeight = _state.CrtControllerRegisters.VerticalTotalValue + 2;
        int verticalDisplayEnd = _state.CrtControllerRegisters.VerticalDisplayEndValue;
        bool verticalTimingHalved = _state.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved;

        _totalScanlines = totalHeight;
        _verticalDisplayEndScanline = verticalDisplayEnd;

        if (verticalTimingHalved) {
            _totalScanlines = totalHeight * 2;
            _verticalDisplayEndScanline = verticalDisplayEnd * 2;
        }

        int horizontalDisplayEnd = _state.CrtControllerRegisters.HorizontalDisplayEnd + 1;
        int skew = _state.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew;
        int totalWidth = _state.CrtControllerRegisters.HorizontalTotal + 3;
        double characterClockDurationMs = HorizontalLineDurationMs / totalWidth;
        _activeDisplayMs = (horizontalDisplayEnd + skew) * characterClockDurationMs;
        _hblankDurationMs = HorizontalLineDurationMs - _activeDisplayMs;
    }

    private void OnFrameStart(uint value) {
        RecomputeTimingParameters();
        _lastFrameStartMs = _clock.ElapsedTimeMs;

        _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
        _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = false;

        _renderer.BeginFrame();

        if (_verticalDisplayEndScanline > 0) {
            _scheduler.AddEvent(OnScanlineActive, 0, 0);
        } else {
            _scheduler.AddEvent(OnVerticalRetraceStart, 0);
        }
    }

    private void OnScanlineActive(uint scanline) {
        _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = false;
        _renderer.RenderScanline();
        _scheduler.AddEvent(OnScanlineHBlank, _activeDisplayMs, scanline);
    }

    private void OnScanlineHBlank(uint scanline) {
        _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = true;

        if (scanline + 1 < (uint)_verticalDisplayEndScanline) {
            _scheduler.AddEvent(OnScanlineActive, _hblankDurationMs, scanline + 1);
        } else {
            _scheduler.AddEvent(OnVerticalRetraceStart, _hblankDurationMs);
        }
    }

    private void OnVerticalRetraceStart(uint value) {
        _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = true;
        _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = true;
        LastFrameDuration = TimeSpan.FromMilliseconds(_clock.ElapsedTimeMs - _lastFrameStartMs);

        _renderer.CompleteFrame();

        int blankingLines = _totalScanlines - _verticalDisplayEndScanline;
        double vBlankDurationMs = blankingLines * HorizontalLineDurationMs;
        _scheduler.AddEvent(OnFrameStart, vBlankDurationMs);
    }

    private void OnBlinkToggle(uint value) {
        _blinkState.IsBlinkPhaseHigh = !_blinkState.IsBlinkPhaseHigh;
        _blinkState.MarkChanged();
        _scheduler.AddEvent(OnBlinkToggle, BlinkPeriodMs);
    }
}
