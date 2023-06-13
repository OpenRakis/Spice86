namespace Spice86.Core.Emulator.Devices.Input.Mouse;

using Serilog.Events;

using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

/// <summary>
///     Basic implementation of a keyboard
/// </summary>
public class Mouse : DefaultIOPortHandler, IMouseDevice {
    private readonly IGui? _gui;
    private readonly ILoggerService _logger;
    private long _lastUpdateTimestamp;
    private bool _previousIsLeftButtonDown;
    private bool _previousIsMiddleButtonDown;
    private bool _previousIsRightButtonDown;
    private double _previousMouseXRelative;
    private double _previousMouseYRelative;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Keyboard" /> class.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="gui">The graphical user interface. Is null in headless mode.</param>
    /// <param name="configuration"></param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Mouse(Machine machine, IGui? gui, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
        _gui = gui;
        MouseType = configuration.Mouse;
        _logger = loggerService.WithLogLevel(LogEventLevel.Verbose);
        Initialize();
    }

    public double DeltaY { get; private set; }

    public double DeltaX { get; private set; }

    public MouseEventMask LastTrigger { get; private set; }
    public ushort SampleRate { get; set; } = 100;
    public MouseType MouseType { get; }
    public double MouseXRelative { get; private set; }
    public double MouseYRelative { get; private set; }
    public bool IsLeftButtonDown { get; private set; }
    public bool IsRightButtonDown { get; private set; }
    public bool IsMiddleButtonDown { get; private set; }
    public ushort DoubleSpeedThreshold { get; set; }
    public ushort HorizontalMickeysPerPixel { get; set; }
    public ushort VerticalMickeysPerPixel { get; set; }

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
        if (!_machine.Cpu.IsRunning || _machine.IsPaused)
            return;
        long timestamp = DateTime.Now.Ticks;
        // Check sample rate to see if we need to send an update yet.
        long ticksDuration = timestamp - _lastUpdateTimestamp;
        long threshold = 10 * TimeSpan.TicksPerSecond / SampleRate;
        if (ticksDuration < threshold) {
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
        _logger.Verbose("Triggering irq 12 from UI thread");
        _machine.DualPic.ProcessInterruptRequest(12);
        // Updated?.Invoke(this, new MouseUpdatedEventArgs(trigger, deltaX, deltaY));
    }
}

[Flags]
public enum MouseEventMask {
    Movement = 1 << 0,
    LeftButtonDown = 1 << 1,
    LeftButtonUp = 1 << 2,
    RightButtonDown = 1 << 3,
    RightButtonUp = 1 << 4,
    MiddleButtonDown = 1 << 5,
    MiddleButtonUp = 1 << 6
}