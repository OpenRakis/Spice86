namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Emulator.Joystick;
using Spice86.Shared.Interfaces;

/// <summary>
/// Emulates the IBM PC Game Adapter (gameport) at I/O port 0x201. <br/>
/// Supports two joysticks, each with two axes and two buttons. <br/>
/// Uses the standard one-shot timer model: writing to port 0x201 fires all axis timers,
/// and reading returns button states (bits 4-7, active low) and axis timer status (bits 0-3). <br/>
/// Axis timer duration is proportional to the joystick position (0.0 to 1.0).
/// </summary>
public class Joystick : DefaultIOPortHandler {
    private const int JoystickPositionAndStatus = 0x201;

    /// <summary>
    /// Whether joystick A is connected. When false, axis timers never expire and buttons read as not pressed.
    /// </summary>
    private bool _joystickAConnected;

    /// <summary>
    /// Whether joystick B is connected.
    /// </summary>
    private bool _joystickBConnected;

    private readonly IEmulatedClock _clock;

    // Joystick A state: axis positions (0.0 to 1.0) and button states
    private double _axisAX = 0.5;
    private double _axisAY = 0.5;
    private bool _buttonA1Pressed;
    private bool _buttonA2Pressed;

    // Joystick B state
    private double _axisBX = 0.5;
    private double _axisBY = 0.5;
    private bool _buttonB1Pressed;
    private bool _buttonB2Pressed;

    // Timestamp (in ms) when port 0x201 was last written, which triggers the one-shot timers.
    private double _lastTriggerTimeMs;

    // Timer expiry times (in ms) for each axis. Set when triggered.
    private double _axisAXExpiryMs;
    private double _axisAYExpiryMs;
    private double _axisBXExpiryMs;
    private double _axisBYExpiryMs;

    /// <summary>
    /// Whether the one-shot timers have been triggered and are potentially still running.
    /// </summary>
    private bool _timersTriggered;

    /// <summary>
    /// Minimum one-shot timer duration in microseconds (24.2 μs for the fixed resistor portion).
    /// </summary>
    private const double MinTimerDurationUs = 24.2;

    /// <summary>
    /// Range of the one-shot timer in microseconds (proportional to the potentiometer value).
    /// A full-scale axis value adds this to the minimum duration, giving ~124.8 μs at maximum.
    /// </summary>
    private const double TimerRangeUs = 100.6;

    /// <summary>
    /// Initializes a new instance of the <see cref="Joystick"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher.</param>
    /// <param name="clock">The emulated clock for timing the one-shot timers.</param>
    /// <param name="failOnUnhandledPort">Whether to throw an exception on unhandled port access.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="joystickEvents">The joystick events from the UI layer.</param>
    public Joystick(State state, IOPortDispatcher ioPortDispatcher, IEmulatedClock clock,
        bool failOnUnhandledPort, ILoggerService loggerService,
        IGuiJoystickEvents? joystickEvents = null) : base(state, failOnUnhandledPort, loggerService) {
        _clock = clock;
        InitPortHandlers(ioPortDispatcher);
        if (joystickEvents is not null) {
            joystickEvents.JoystickAStateChanged += OnJoystickAStateChanged;
            joystickEvents.JoystickBStateChanged += OnJoystickBStateChanged;
        }
    }

    private void OnJoystickAStateChanged(object? sender, JoystickStateEventArgs e) {
        _axisAX = e.AxisX;
        _axisAY = e.AxisY;
        _buttonA1Pressed = e.Button1Pressed;
        _buttonA2Pressed = e.Button2Pressed;
        _joystickAConnected = true;
    }

    private void OnJoystickBStateChanged(object? sender, JoystickStateEventArgs e) {
        _axisBX = e.AxisX;
        _axisBY = e.AxisY;
        _buttonB1Pressed = e.Button1Pressed;
        _buttonB2Pressed = e.Button2Pressed;
        _joystickBConnected = true;
    }

    /// <summary>
    /// Gets the current state of joystick A.
    /// </summary>
    public JoystickSnapshot GetJoystickAState() {
        return new JoystickSnapshot(_joystickAConnected, _axisAX, _axisAY, _buttonA1Pressed, _buttonA2Pressed);
    }

    /// <summary>
    /// Gets the current state of joystick B.
    /// </summary>
    public JoystickSnapshot GetJoystickBState() {
        return new JoystickSnapshot(_joystickBConnected, _axisBX, _axisBY, _buttonB1Pressed, _buttonB2Pressed);
    }

    /// <summary>
    /// Sets the state of joystick A directly (bypassing events).
    /// Marks joystick A as connected.
    /// </summary>
    public void SetJoystickAState(double axisX, double axisY, bool button1, bool button2) {
        _axisAX = Math.Clamp(axisX, 0.0, 1.0);
        _axisAY = Math.Clamp(axisY, 0.0, 1.0);
        _buttonA1Pressed = button1;
        _buttonA2Pressed = button2;
        _joystickAConnected = true;
    }

    /// <summary>
    /// Sets the state of joystick B directly (bypassing events).
    /// Marks joystick B as connected.
    /// </summary>
    public void SetJoystickBState(double axisX, double axisY, bool button1, bool button2) {
        _axisBX = Math.Clamp(axisX, 0.0, 1.0);
        _axisBY = Math.Clamp(axisY, 0.0, 1.0);
        _buttonB1Pressed = button1;
        _buttonB2Pressed = button2;
        _joystickBConnected = true;
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(JoystickPositionAndStatus, this);
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (port != JoystickPositionAndStatus) {
            return base.ReadByte(port);
        }

        byte result = 0;

        // Bits 0-3: axis timer status (1 = timer still running)
        if (_timersTriggered) {
            double currentTimeMs = _clock.ElapsedTimeMs;
            if (_joystickAConnected) {
                if (currentTimeMs < _axisAXExpiryMs) {
                    result |= 0x01;
                }
                if (currentTimeMs < _axisAYExpiryMs) {
                    result |= 0x02;
                }
            }
            if (_joystickBConnected) {
                if (currentTimeMs < _axisBXExpiryMs) {
                    result |= 0x04;
                }
                if (currentTimeMs < _axisBYExpiryMs) {
                    result |= 0x08;
                }
            }
            // If no joystick is connected, the corresponding bits stay at 1 (timer never expires)
            if (!_joystickAConnected) {
                result |= 0x03;
            }
            if (!_joystickBConnected) {
                result |= 0x0C;
            }
        }

        // Bits 4-7: button states (0 = pressed, 1 = not pressed)
        if (!_buttonA1Pressed) {
            result |= 0x10;
        }
        if (!_buttonA2Pressed) {
            result |= 0x20;
        }
        if (!_buttonB1Pressed) {
            result |= 0x40;
        }
        if (!_buttonB2Pressed) {
            result |= 0x80;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Joystick read port 0x{Port:X4} = 0x{Value:X2}", port, result);
        }

        return result;
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (port != JoystickPositionAndStatus) {
            base.WriteByte(port, value);
            return;
        }

        // Writing any value to port 0x201 triggers the one-shot timers.
        _lastTriggerTimeMs = _clock.ElapsedTimeMs;
        _timersTriggered = true;

        // Calculate expiry time for each axis based on current joystick position.
        // Timer duration = MinTimerDurationUs + (axisValue * TimerRangeUs) in microseconds.
        _axisAXExpiryMs = _lastTriggerTimeMs + AxisPositionToTimerDurationMs(_axisAX);
        _axisAYExpiryMs = _lastTriggerTimeMs + AxisPositionToTimerDurationMs(_axisAY);
        _axisBXExpiryMs = _lastTriggerTimeMs + AxisPositionToTimerDurationMs(_axisBX);
        _axisBYExpiryMs = _lastTriggerTimeMs + AxisPositionToTimerDurationMs(_axisBY);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Joystick one-shot timers triggered at {Time}ms", _lastTriggerTimeMs);
        }
    }

    /// <summary>
    /// Converts an axis position (0.0 to 1.0) to a timer duration in milliseconds.
    /// </summary>
    private static double AxisPositionToTimerDurationMs(double axisPosition) {
        double durationUs = MinTimerDurationUs + (axisPosition * TimerRangeUs);
        return durationUs / 1000.0;
    }
}