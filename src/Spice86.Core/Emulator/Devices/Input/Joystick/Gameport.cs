namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Emulates the IBM PC gameport at I/O port
/// <see cref="GameportConstants.Port201"/>, replacing the previous
/// stub <c>Joystick</c> handler. Behaviour is modelled after
/// DOSBox Staging's <c>read_p201_timed</c>/<c>write_p201_timed</c>.
/// </summary>
/// <remarks>
/// The device is intentionally free of UI/SDL dependencies: the
/// per-frame stick state comes from an injected
/// <see cref="IGameportInputSource"/>. In headless mode the source
/// is a <see cref="NullJoystickInput"/>; in the Avalonia UI it is
/// an SDL-backed adapter; in tests it is a fake. Force-feedback
/// requests can optionally be forwarded via <see cref="IRumbleSink"/>.
/// </remarks>
public sealed class Gameport : DefaultIOPortHandler, IGameportPortReader {
    private readonly IGameportInputSource _inputSource;
    private readonly IRumbleSink? _rumbleSink;
    private readonly ITimeProvider _timeProvider;
    private readonly DateTime _epoch;

    /// <summary>
    /// Initializes the gameport, registers the
    /// <see cref="GameportConstants.Port201"/> handler, and starts
    /// in a disarmed state.
    /// </summary>
    /// <param name="state">CPU state, forwarded to
    /// <see cref="DefaultIOPortHandler"/>.</param>
    /// <param name="ioPortDispatcher">Dispatcher this device
    /// registers itself with.</param>
    /// <param name="inputSource">Source of the live virtual stick
    /// state. Use <see cref="NullJoystickInput"/> when no UI/SDL
    /// adapter is available.</param>
    /// <param name="timeProvider">Time provider, used as the
    /// monotonic clock for the RC decay timer.</param>
    /// <param name="rumbleSink">Optional sink for force-feedback
    /// requests. Pass <see langword="null"/> to silently drop them.</param>
    /// <param name="failOnUnhandledPort">Whether unhandled port
    /// accesses throw.</param>
    /// <param name="loggerService">Logger service implementation.</param>
    public Gameport(
        State state,
        IOPortDispatcher ioPortDispatcher,
        IGameportInputSource inputSource,
        ITimeProvider timeProvider,
        IRumbleSink? rumbleSink,
        bool failOnUnhandledPort,
        ILoggerService loggerService)
        : base(state, failOnUnhandledPort, loggerService) {
        _inputSource = inputSource;
        _timeProvider = timeProvider;
        _rumbleSink = rumbleSink;
        _epoch = timeProvider.Now;
        Timer = new GameportTimer();
        ioPortDispatcher.AddIOPortHandler(GameportConstants.Port201, this);
        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information(
                "JOYSTICK: gameport device installed at 0x{Port:X4} (input source: {Source})",
                GameportConstants.Port201,
                inputSource.DisplayName);
        }
    }

    /// <summary>
    /// The internal RC-decay timer. Exposed so that the mapper UI
    /// and MCP tools can render its current state for diagnostics.
    /// </summary>
    public GameportTimer Timer { get; }

    /// <summary>
    /// The currently-attached input source. Exposed so that the
    /// mapper UI can display the source name and the MCP layer can
    /// route <c>joystick_*</c> tool calls to it.
    /// </summary>
    public IGameportInputSource InputSource => _inputSource;

    /// <summary>
    /// Optional rumble sink. <see langword="null"/> when no haptic
    /// output is wired (headless mode, tests, or a controller
    /// without rumble support).
    /// </summary>
    public IRumbleSink? RumbleSink => _rumbleSink;

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
        VirtualJoystickState state = _inputSource.GetCurrentState();
        Timer.Arm(state, ElapsedMs());
    }

    /// <inheritdoc />
    public byte PeekPort201() {
        return ComputePort201Byte();
    }

    private byte ComputePort201Byte() {
        VirtualJoystickState state = _inputSource.GetCurrentState();
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
