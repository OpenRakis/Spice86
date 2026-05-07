namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Emulates the IBM PC gameport at I/O port
/// <see cref="GameportConstants.Port201"/>. Behaviour matches DOSBox
/// Staging's <c>read_p201_timed</c>/<c>write_p201_timed</c>.
/// </summary>
/// <remarks>
/// Input flows through the same pipeline as keyboard and mouse:
/// the UI raises logical <c>IGuiJoystickEvents</c> (raw SDL events
/// translated through the active <c>JoystickProfile</c> on the UI
/// thread), <c>InputEventHub</c> queues them, and they are replayed
/// on the emulator thread where this device subscribes. The Core
/// is therefore independent of SDL/Avalonia; in headless mode the
/// same events are produced by <c>HeadlessGui</c> or a scripted
/// harness.
/// </remarks>
public sealed class Gameport : DefaultIOPortHandler, IGameportPortReader, IDisposable {
    private readonly IGuiJoystickEvents _joystickEvents;
    private readonly RumbleRouter? _rumbleRouter;
    private readonly MidiOnGameportRouter? _midiRouter;
    private readonly ITimeProvider _timeProvider;
    private readonly DateTime _epoch;
    private readonly object _stateLock = new();

    private VirtualStickState _stickA = VirtualStickState.Disconnected;
    private VirtualStickState _stickB = VirtualStickState.Disconnected;

    /// <summary>
    /// Initializes the gameport, registers the
    /// <see cref="GameportConstants.Port201"/> handler and
    /// subscribes to the supplied joystick event source.
    /// </summary>
    /// <param name="state">CPU state, forwarded to
    /// <see cref="DefaultIOPortHandler"/>.</param>
    /// <param name="ioPortDispatcher">Dispatcher this device
    /// registers itself with.</param>
    /// <param name="joystickEvents">Source of logical joystick
    /// events. In production this is the <c>InputEventHub</c>
    /// (queued, emulator-thread); in tests it is a fake that
    /// raises events synchronously.</param>
    /// <param name="timeProvider">Time provider used as the
    /// monotonic clock for the RC decay timer.</param>
    /// <param name="rumbleRouter">Optional router for force-feedback
    /// requests. Pass <see langword="null"/> when no haptic stack is
    /// wired (headless mode, tests, controller without rumble).</param>
    /// <param name="midiRouter">Optional MIDI-on-gameport router.
    /// When non-null, every byte written to port <c>0x201</c> is
    /// forwarded after the timer-arm step; the router itself decides
    /// whether to deliver based on the active profile.</param>
    /// <param name="failOnUnhandledPort">Whether unhandled port
    /// accesses throw.</param>
    /// <param name="loggerService">Logger service implementation.</param>
    public Gameport(
        State state,
        IOPortDispatcher ioPortDispatcher,
        IGuiJoystickEvents joystickEvents,
        ITimeProvider timeProvider,
        RumbleRouter? rumbleRouter,
        MidiOnGameportRouter? midiRouter,
        bool failOnUnhandledPort,
        ILoggerService loggerService)
        : base(state, failOnUnhandledPort, loggerService) {
        _joystickEvents = joystickEvents;
        _timeProvider = timeProvider;
        _rumbleRouter = rumbleRouter;
        _midiRouter = midiRouter;
        _epoch = timeProvider.Now;
        Timer = new GameportTimer();
        _joystickEvents.JoystickAxisChanged += OnAxisChanged;
        _joystickEvents.JoystickButtonChanged += OnButtonChanged;
        _joystickEvents.JoystickHatChanged += OnHatChanged;
        _joystickEvents.JoystickConnectionChanged += OnConnectionChanged;
        ioPortDispatcher.AddIOPortHandler(GameportConstants.Port201, this);
        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information(
                "JOYSTICK: gameport device installed at 0x{Port:X4}",
                GameportConstants.Port201);
        }
    }

    /// <summary>
    /// The internal RC-decay timer. Exposed so the mapper UI and
    /// MCP tools can render its current state for diagnostics.
    /// </summary>
    public GameportTimer Timer { get; }

    /// <summary>
    /// Optional rumble router. <see langword="null"/> when no haptic
    /// stack is wired (headless mode, tests, or a controller without
    /// rumble support).
    /// </summary>
    public RumbleRouter? RumbleRouter => _rumbleRouter;

    /// <summary>
    /// Optional MIDI-on-gameport router. <see langword="null"/> when
    /// no MPU-401 sink is wired.
    /// </summary>
    public MidiOnGameportRouter? MidiRouter => _midiRouter;

    /// <summary>
    /// Returns an immutable snapshot of the current two-stick state
    /// as built up from the joystick event stream.
    /// </summary>
    public VirtualJoystickState GetCurrentState() {
        lock (_stateLock) {
            return new VirtualJoystickState(_stickA, _stickB);
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (port != GameportConstants.Port201) {
            return base.ReadByte(port);
        }
        return ComputePort201Byte();
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (port != GameportConstants.Port201) {
            base.WriteByte(port, value);
            return;
        }
        Timer.Arm(GetCurrentState(), ElapsedMs());
        _midiRouter?.OnGameportWrite(value);
    }

    /// <inheritdoc />
    public byte PeekPort201() {
        return ComputePort201Byte();
    }

    /// <inheritdoc />
    public void Dispose() {
        _joystickEvents.JoystickAxisChanged -= OnAxisChanged;
        _joystickEvents.JoystickButtonChanged -= OnButtonChanged;
        _joystickEvents.JoystickHatChanged -= OnHatChanged;
        _joystickEvents.JoystickConnectionChanged -= OnConnectionChanged;
    }

    private void OnAxisChanged(object? sender, JoystickAxisEventArgs e) {
        lock (_stateLock) {
            if (e.StickIndex == 0) {
                _stickA = ApplyAxis(_stickA, e.Axis, e.Value);
            } else if (e.StickIndex == 1) {
                _stickB = ApplyAxis(_stickB, e.Axis, e.Value);
            }
        }
    }

    private void OnButtonChanged(object? sender, JoystickButtonEventArgs e) {
        if (e.ButtonIndex < 0 || e.ButtonIndex > 3) {
            return;
        }
        lock (_stateLock) {
            if (e.StickIndex == 0) {
                _stickA = ApplyButton(_stickA, e.ButtonIndex, e.IsPressed);
            } else if (e.StickIndex == 1) {
                _stickB = ApplyButton(_stickB, e.ButtonIndex, e.IsPressed);
            }
        }
    }

    private void OnHatChanged(object? sender, JoystickHatEventArgs e) {
        lock (_stateLock) {
            if (e.StickIndex == 0) {
                _stickA = _stickA with { Hat = e.Direction };
            } else if (e.StickIndex == 1) {
                _stickB = _stickB with { Hat = e.Direction };
            }
        }
    }

    private void OnConnectionChanged(object? sender, JoystickConnectionEventArgs e) {
        lock (_stateLock) {
            if (e.StickIndex == 0) {
                if (e.IsConnected) {
                    _stickA = _stickA with { IsConnected = true };
                } else {
                    _stickA = VirtualStickState.Disconnected;
                }
            } else if (e.StickIndex == 1) {
                if (e.IsConnected) {
                    _stickB = _stickB with { IsConnected = true };
                } else {
                    _stickB = VirtualStickState.Disconnected;
                }
            }
        }
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            string transition;
            if (e.IsConnected) {
                transition = "connected";
            } else {
                transition = "disconnected";
            }
            _loggerService.Information(
                "JOYSTICK: stick {Index} {State} (device: {Name})",
                e.StickIndex, transition, e.DeviceName);
        }
    }

    private static VirtualStickState ApplyAxis(VirtualStickState stick, JoystickAxis axis, float value) {
        if (axis == JoystickAxis.X) {
            return stick with { X = value };
        }
        if (axis == JoystickAxis.Y) {
            return stick with { Y = value };
        }
        if (axis == JoystickAxis.Z) {
            return stick with { Z = value };
        }
        return stick with { R = value };
    }

    private static VirtualStickState ApplyButton(VirtualStickState stick, int buttonIndex, bool pressed) {
        byte mask = (byte)(1 << buttonIndex);
        byte newButtons;
        if (pressed) {
            newButtons = (byte)(stick.Buttons | mask);
        } else {
            newButtons = (byte)(stick.Buttons & ~mask);
        }
        return stick with { Buttons = newButtons };
    }

    private byte ComputePort201Byte() {
        VirtualJoystickState state = GetCurrentState();
        double nowMs = ElapsedMs();
        if (!Timer.IsInsideArmedWindow(nowMs)) {
            Timer.Disarm();
        }
        byte ret = GameportConstants.UnpluggedReading;
        if (state.StickA.IsConnected) {
            if (!Timer.IsStickAxActive(nowMs)) {
                ret = ClearBit(ret, GameportConstants.BitStickAxAxis);
            }
            if (!Timer.IsStickAyActive(nowMs)) {
                ret = ClearBit(ret, GameportConstants.BitStickAyAxis);
            }
            if (state.StickA.IsButtonPressed(0)) {
                ret = ClearBit(ret, GameportConstants.BitStickAButton1);
            }
            if (state.StickA.IsButtonPressed(1)) {
                ret = ClearBit(ret, GameportConstants.BitStickAButton2);
            }
        }
        if (state.StickB.IsConnected) {
            if (!Timer.IsStickBxActive(nowMs)) {
                ret = ClearBit(ret, GameportConstants.BitStickBxAxis);
            }
            if (!Timer.IsStickByActive(nowMs)) {
                ret = ClearBit(ret, GameportConstants.BitStickByAxis);
            }
            if (state.StickB.IsButtonPressed(0)) {
                ret = ClearBit(ret, GameportConstants.BitStickBButton1);
            }
            if (state.StickB.IsButtonPressed(1)) {
                ret = ClearBit(ret, GameportConstants.BitStickBButton2);
            }
        }
        return ret;
    }

    private static byte ClearBit(byte value, byte bit) {
        return unchecked((byte)(value & ~bit));
    }

    private double ElapsedMs() {
        return (_timeProvider.Now - _epoch).TotalMilliseconds;
    }
}
