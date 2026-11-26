using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.Devices.Input.Mouse;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

/// <summary>
///     Basic implementation of a mouse
/// </summary>
public class Mouse : DefaultIOPortHandler, IMouseDevice {
    private const int IrqNumber = 12;
    private readonly IGuiMouseEvents? _gui;
    private readonly ILogger _logger;
    private readonly IEmulatedClock _clock;
    private readonly DeviceScheduler _scheduler;
    private double _lastUpdateTimeMs = double.NegativeInfinity;
    private bool _updateScheduled;
    private MouseEventMask _pendingTrigger;
    private bool _previousIsLeftButtonDown;
    private bool _previousIsMiddleButtonDown;
    private bool _previousIsRightButtonDown;
    private double _previousMouseXRelative;
    private double _previousMouseYRelative;
    private int _sampleRate = 100;
    private readonly DualPic _dualPic;
    private readonly SharedMouseData _sharedMouseData;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Mouse" /> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="sharedMouseData">The mouse data shared with the UI for display.</param>
    /// <param name="dualPic">The two Programmable Interrupt Controllers.</param>
    /// <param name="mouseType">The type of mouse to emulate.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O wasn't handled.</param>
    /// <param name="clock">Clock used to pace mouse samples.</param>
    /// <param name="scheduler">Emulator-thread scheduler for deferred mouse samples.</param>
    /// <param name="gui">The GUI mouse events interface, or null if not using a GUI.</param>
    public Mouse(State state, SharedMouseData sharedMouseData,
        DualPic dualPic, MouseType mouseType,
        ILogger loggerService, bool failOnUnhandledPort, IEmulatedClock clock, DeviceScheduler scheduler,
        IGuiMouseEvents? gui = null)
        : base(state, failOnUnhandledPort, loggerService) {
        _gui = gui;
        _sharedMouseData = sharedMouseData;
        _dualPic = dualPic;
        MouseType = mouseType;
        _logger = loggerService;
        _clock = clock;
        _scheduler = scheduler;
        Initialize();
        _dualPic.SetIrqMask(IrqNumber, false);
    }

    /// <inheritdoc />
    public double DeltaY { get; private set; }

    /// <inheritdoc />
    public double DeltaX { get; private set; }

    /// <inheritdoc />
    public int ButtonCount => 3;

    /// <inheritdoc />
    public double MouseXRelative { get => _sharedMouseData.MouseXRelative; set => _sharedMouseData.MouseXRelative = value; }

    /// <inheritdoc />
    public double MouseYRelative { get => _sharedMouseData.MouseYRelative; set => _sharedMouseData.MouseYRelative = value; }

    /// <inheritdoc />
    public MouseEventMask LastTrigger { get; private set; }

    /// <inheritdoc />
    public int SampleRate {
        get => _sampleRate;
        set {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _sampleRate = value;
        }
    }

    /// <inheritdoc />
    public MouseType MouseType { get; }

    /// <inheritdoc />
    public bool IsLeftButtonDown { get => _sharedMouseData.IsLeftButtonDown; private set => _sharedMouseData.IsLeftButtonDown = value; }

    /// <inheritdoc />
    public bool IsRightButtonDown { get => _sharedMouseData.IsRightButtonDown; private set => _sharedMouseData.IsRightButtonDown = value; }

    /// <inheritdoc />
    public bool IsMiddleButtonDown { get => _sharedMouseData.IsMiddleButtonDown; private set => _sharedMouseData.IsMiddleButtonDown = value; }

    /// <inheritdoc />
    public int DoubleSpeedThreshold { get; set; }

    /// <inheritdoc />
    public int HorizontalMickeysPerPixel { get; set; } = 8;

    /// <inheritdoc />
    public int VerticalMickeysPerPixel { get; set; } = 16;

    private void Initialize() {
        if (_gui is not null && MouseType != MouseType.None) {
            _gui.MouseButtonUp += OnMouseClick;
            _gui.MouseButtonDown += OnMouseClick;
            _gui.MouseMoved += OnMouseMoved;
        }
        if (_logger.IsEnabled(LogLevel.Information)) {
            _logger.LogInformation("Mouse initialized: {MouseType}", MouseType);
        }
    }

    private void OnMouseMoved(object? sender, MouseMoveEventArgs eventArgs) {
        MouseXRelative = eventArgs.X;
        MouseYRelative = eventArgs.Y;
        UpdateMouse();
    }

    private void OnMouseClick(object? sender, MouseButtonEventArgs eventArgs) {
        switch (eventArgs.Button) {
            case MouseButton.Left:
                IsLeftButtonDown = eventArgs.ButtonDown;
                break;
            case MouseButton.Right:
                IsRightButtonDown = eventArgs.ButtonDown;
                break;
            case MouseButton.Middle:
                IsMiddleButtonDown = eventArgs.ButtonDown;
                break;
            case MouseButton.None:
            case MouseButton.XButton1:
            case MouseButton.XButton2:
            default: {
                    if (_logger.IsEnabled(LogLevel.Information)) {
                        _logger.LogInformation("Unknown mouse button clicked: {@EventArgs}", eventArgs);
                        return;
                    }
                    break;
                }
        }
        UpdateMouse();
    }

    private void UpdateMouse() {
        MouseEventMask trigger = 0;
        double deltaX = MouseXRelative - _previousMouseXRelative;
        double deltaY = MouseYRelative - _previousMouseYRelative;
        DeltaX += deltaX;
        DeltaY += deltaY;
        if (Math.Abs(deltaX) > double.Epsilon || Math.Abs(deltaY) > double.Epsilon) {
            trigger |= MouseEventMask.Movement;
        }
        _previousMouseXRelative = MouseXRelative;
        _previousMouseYRelative = MouseYRelative;

        if (IsLeftButtonDown != _previousIsLeftButtonDown) {
            trigger |= IsLeftButtonDown ? MouseEventMask.LeftButtonDown : MouseEventMask.LeftButtonUp;
        }
        _previousIsLeftButtonDown = IsLeftButtonDown;

        if (IsRightButtonDown != _previousIsRightButtonDown) {
            trigger |= IsRightButtonDown ? MouseEventMask.RightButtonDown : MouseEventMask.RightButtonUp;
        }
        _previousIsRightButtonDown = IsRightButtonDown;

        if (IsMiddleButtonDown != _previousIsMiddleButtonDown) {
            trigger |= IsMiddleButtonDown ? MouseEventMask.MiddleButtonDown : MouseEventMask.MiddleButtonUp;
        }
        _previousIsMiddleButtonDown = IsMiddleButtonDown;

        _pendingTrigger |= trigger;
        if (_pendingTrigger == 0) {
            return;
        }

        double now = _clock.ElapsedTimeMs;
        double delay = _lastUpdateTimeMs + 1000.0 / _sampleRate - now;
        if (delay > 0) {
            if (!_updateScheduled) {
                _scheduler.AddEvent(DeliverPendingUpdate, delay);
                _updateScheduled = true;
            }
            return;
        }

        if (_updateScheduled) {
            _scheduler.RemoveEvents(DeliverPendingUpdate);
            _updateScheduled = false;
        }
        _lastUpdateTimeMs = now;
        LastTrigger = _pendingTrigger;
        _pendingTrigger = 0;
        TriggerInterruptRequest();
    }

    private void DeliverPendingUpdate(uint _) {
        _updateScheduled = false;
        UpdateMouse();
    }

    private void TriggerInterruptRequest() {
        _dualPic.ProcessInterruptRequest(IrqNumber);
    }

    /// <inheritdoc />
    public void ResetDeltas() {
        DeltaX = 0;
        DeltaY = 0;
    }

    /// <summary>
    ///     Captures the state of the mouse device.
    /// </summary>
    /// <returns>The state of the device.</returns>
    public MouseDeviceState CaptureState() {
        double now = _clock.ElapsedTimeMs;
        double? pendingSampleTime = _scheduler.GetNextEventTime(DeliverPendingUpdate);
        double? pendingSampleDelay = pendingSampleTime is null ? null : pendingSampleTime.Value - now;
        double? lastSampleTime = double.IsNegativeInfinity(_lastUpdateTimeMs) ? null : _lastUpdateTimeMs;
        return new MouseDeviceState(
            MouseXRelative,
            MouseYRelative,
            IsLeftButtonDown,
            IsRightButtonDown,
            IsMiddleButtonDown,
            DeltaX,
            DeltaY,
            LastTrigger,
            _pendingTrigger,
            _previousMouseXRelative,
            _previousMouseYRelative,
            _previousIsLeftButtonDown,
            _previousIsRightButtonDown,
            _previousIsMiddleButtonDown,
            _sampleRate,
            DoubleSpeedThreshold,
            HorizontalMickeysPerPixel,
            VerticalMickeysPerPixel,
            now,
            lastSampleTime,
            pendingSampleDelay);
    }

    /// <summary>
    ///     Puts the mouse device back in a captured state.
    /// </summary>
    /// <remarks>
    ///     The time of the last sample is moved by the time that passed on the emulated clock since the capture,
    ///     so the next sample waits as long as it waited at capture. The sample event is removed from the
    ///     scheduler and queued again as far ahead as it was at capture. The IRQ 12 line is not touched: restore
    ///     the interrupt controller's state for that.
    /// </remarks>
    /// <param name="state">State captured by <see cref="CaptureState" />.</param>
    public void RestoreState(MouseDeviceState state) {
        double elapsedSinceCapture = _clock.ElapsedTimeMs - state.CapturedAtMs;
        MouseXRelative = state.MouseXRelative;
        MouseYRelative = state.MouseYRelative;
        IsLeftButtonDown = state.IsLeftButtonDown;
        IsRightButtonDown = state.IsRightButtonDown;
        IsMiddleButtonDown = state.IsMiddleButtonDown;
        DeltaX = state.DeltaX;
        DeltaY = state.DeltaY;
        LastTrigger = state.LastTrigger;
        _pendingTrigger = state.PendingTrigger;
        _previousMouseXRelative = state.PreviousMouseXRelative;
        _previousMouseYRelative = state.PreviousMouseYRelative;
        _previousIsLeftButtonDown = state.PreviousIsLeftButtonDown;
        _previousIsRightButtonDown = state.PreviousIsRightButtonDown;
        _previousIsMiddleButtonDown = state.PreviousIsMiddleButtonDown;
        SampleRate = state.SampleRate;
        DoubleSpeedThreshold = state.DoubleSpeedThreshold;
        HorizontalMickeysPerPixel = state.HorizontalMickeysPerPixel;
        VerticalMickeysPerPixel = state.VerticalMickeysPerPixel;
        _lastUpdateTimeMs = double.NegativeInfinity;
        if (state.LastSampleTimeMs is { } lastSampleTime) {
            _lastUpdateTimeMs = lastSampleTime + elapsedSinceCapture;
        }

        _scheduler.RemoveEvents(DeliverPendingUpdate);
        _updateScheduled = state.PendingSampleDelayMs is not null;
        if (state.PendingSampleDelayMs is { } pendingSampleDelay) {
            _scheduler.AddEvent(DeliverPendingUpdate, pendingSampleDelay);
        }
    }
}
