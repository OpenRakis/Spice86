namespace Spice86.Core.Emulator.Devices.Input.Mouse;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

/// <summary>
///     Basic implementation of a mouse
/// </summary>
public class Mouse : DefaultIOPortHandler, IMouseDevice {
    private const int IrqNumber = 12;
    private readonly IGui? _gui;
    private readonly ILoggerService _logger;
    private long _lastUpdateTimestamp;
    private bool _previousIsLeftButtonDown;
    private bool _previousIsMiddleButtonDown;
    private bool _previousIsRightButtonDown;
    private double _previousMouseXRelative;
    private double _previousMouseYRelative;
    private int _sampleRate = 100;
    private long _sampleRateTicks;
    private readonly DualPic _dualPic;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Mouse" /> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="dualPic">The two Programmable Interrupt Controllers.</param>
    /// <param name="gui">The graphical user interface. Is null in headless mode.</param>
    /// <param name="mouseType">The type of mouse to emulate.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O wasn't handled.</param>
    public Mouse(ICpuState state, DualPic dualPic, IGui? gui, MouseType mouseType, ILoggerService loggerService, bool failOnUnhandledPort) : base(state, failOnUnhandledPort, loggerService) {
        _gui = gui;
        _dualPic = dualPic;
        MouseType = mouseType;
        _logger = loggerService;
        _sampleRateTicks = TimeSpan.TicksPerSecond / _sampleRate;
        Initialize();
    }

    /// <inheritdoc />
    public double DeltaY { get; private set; }

    /// <inheritdoc />
    public double DeltaX { get; private set; }

    /// <inheritdoc />
    public int ButtonCount => 3;

    /// <inheritdoc />
    public double MouseXRelative { get; set; }

    /// <inheritdoc />
    public double MouseYRelative { get; set; }

    /// <inheritdoc />
    public MouseEventMask LastTrigger { get; private set; }

    /// <inheritdoc />
    public int SampleRate {
        get => _sampleRate;
        set {
            _sampleRate = value;
            _sampleRateTicks = TimeSpan.TicksPerSecond / _sampleRate;
        }
    }

    /// <inheritdoc />
    public MouseType MouseType { get; }

    /// <inheritdoc />
    public bool IsLeftButtonDown { get; private set; }

    /// <inheritdoc />
    public bool IsRightButtonDown { get; private set; }

    /// <inheritdoc />
    public bool IsMiddleButtonDown { get; private set; }

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
        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information("Mouse initialized: {MouseType}", MouseType);
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
                if (_logger.IsEnabled(LogEventLevel.Information)) {
                    _logger.Information("Unknown mouse button clicked: {@EventArgs}", eventArgs);
                    return;
                }
                break;
            }
        }
        UpdateMouse();
    }

    private void UpdateMouse() {
        long timestamp = DateTime.Now.Ticks;
        // Check sample rate to see if we need to send an update yet.
        long ticksElapsed = timestamp - _lastUpdateTimestamp;
        if (ticksElapsed < _sampleRateTicks) {
            return;
        }
        _lastUpdateTimestamp = timestamp;

        MouseEventMask trigger = 0;
        DeltaX = MouseXRelative - _previousMouseXRelative;
        DeltaY = MouseYRelative - _previousMouseYRelative;
        if (Math.Abs(DeltaX) > double.Epsilon || Math.Abs(DeltaY) > double.Epsilon) {
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

        LastTrigger = trigger;
        TriggerInterruptRequest();
    }

    private void TriggerInterruptRequest() {
        _dualPic.ProcessInterruptRequest(IrqNumber);
    }
}