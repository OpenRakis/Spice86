namespace Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;

using Serilog.Events;

using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

/// <summary>
/// Routes byte writes intercepted on gameport I/O port <c>0x201</c>
/// to the MPU-401 data port when the active
/// <see cref="MidiOnGameportSettings"/> profile enables the feature.
/// </summary>
/// <remarks>
/// This mirrors DOSBox Staging's gameport-MIDI behaviour: a small
/// number of titles (e.g. early Sierra games shipped with the Roland
/// MT-32 starter pack) drive a daughterboard MIDI adapter through
/// the gameport rather than through dedicated MPU-401 ports. When
/// enabled, every <c>OUT 0x201</c> byte is forwarded to the
/// MPU-401's data port (base + 0); the normal joystick-decay timer
/// is unaffected and continues to run alongside the routing.
/// </remarks>
public sealed class MidiOnGameportRouter {
    /// <summary>
    /// Default MPU-401 base I/O port used when
    /// <see cref="MidiOnGameportSettings.Mpu401BasePort"/> is unset.
    /// </summary>
    public const int DefaultMpu401BasePort = 0x330;

    private readonly IMpu401DataSink? _sink;
    private readonly ILoggerService _loggerService;
    private readonly object _lock = new();

    private bool _enabled;
    private int _basePort = DefaultMpu401BasePort;

    /// <summary>
    /// Initializes a new <see cref="MidiOnGameportRouter"/>.
    /// </summary>
    /// <param name="sink">MPU-401 data sink. May be
    /// <see langword="null"/> when the host has no MIDI device, in
    /// which case all writes are silently dropped.</param>
    /// <param name="loggerService">Logger used for the
    /// enable/disable transition messages.</param>
    public MidiOnGameportRouter(IMpu401DataSink? sink, ILoggerService loggerService) {
        _sink = sink;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Whether routing is currently enabled.
    /// </summary>
    public bool IsEnabled {
        get {
            lock (_lock) {
                return _enabled;
            }
        }
    }

    /// <summary>
    /// Active MPU-401 base port. Equals the configured override or
    /// <see cref="DefaultMpu401BasePort"/> when unset.
    /// </summary>
    public int Mpu401BasePort {
        get {
            lock (_lock) {
                return _basePort;
            }
        }
    }

    /// <summary>
    /// Applies the gameport-MIDI section of a joystick profile.
    /// A <see langword="null"/> argument disables routing.
    /// </summary>
    /// <param name="settings">Settings to apply, or
    /// <see langword="null"/> to disable.</param>
    public void Configure(MidiOnGameportSettings? settings) {
        bool wasEnabled;
        bool isEnabled;
        int port;
        lock (_lock) {
            wasEnabled = _enabled;
            if (settings is null) {
                _enabled = false;
                _basePort = DefaultMpu401BasePort;
            } else {
                _enabled = settings.Enabled;
                _basePort = settings.Mpu401BasePort ?? DefaultMpu401BasePort;
            }
            isEnabled = _enabled;
            port = _basePort;
        }
        if (wasEnabled == isEnabled) {
            return;
        }
        if (isEnabled) {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information(
                    "JOYSTICK: MIDI-on-gameport enabled (MPU-401 data=0x{Port:X3})", port);
            }
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("JOYSTICK: MIDI-on-gameport disabled");
            }
        }
    }

    /// <summary>
    /// Forwards a gameport write to the MPU-401 data port when
    /// routing is enabled and a sink is wired.
    /// </summary>
    /// <param name="value">Byte the guest wrote to port
    /// <c>0x201</c>.</param>
    /// <returns><see langword="true"/> when the byte was forwarded;
    /// <see langword="false"/> when routing is disabled or no sink
    /// is wired.</returns>
    public bool OnGameportWrite(byte value) {
        bool enabled;
        int port;
        lock (_lock) {
            enabled = _enabled;
            port = _basePort;
        }
        if (!enabled || _sink is null) {
            return false;
        }
        _sink.WriteData(port, value);
        return true;
    }
}
