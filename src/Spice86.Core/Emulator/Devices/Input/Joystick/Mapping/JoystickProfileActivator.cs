namespace Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;

using Serilog.Events;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Listens for joystick connect/disconnect events and applies the
/// matching <see cref="JoystickProfile"/> to the
/// <see cref="MidiOnGameportRouter"/> and <see cref="RumbleRouter"/>
/// so the active profile's MIDI-on-gameport and rumble settings
/// take effect as soon as a stick is connected.
/// </summary>
/// <remarks>
/// Profile resolution uses
/// <see cref="JoystickProfileAutoLoader.Resolve"/> with the
/// catalogue obtained from
/// <see cref="JoystickProfileAutoLoader.LoadAll"/> at startup.
/// GUID-based matching is left to the SDL-aware UI adapter, which
/// will eventually surface the joystick GUID through a richer
/// connection event; for now only the device name is available, so
/// only name / DefaultProfileName / embedded fallback matching
/// participates here. On disconnect both routers are reset so a
/// previously-connected stick cannot leave MIDI-on-gameport or
/// rumble enabled.
/// </remarks>
public sealed class JoystickProfileActivator : IDisposable {
    private readonly IGuiJoystickEvents _events;
    private readonly JoystickProfileAutoLoader _autoLoader;
    private readonly LoadedProfiles _loaded;
    private readonly MidiOnGameportRouter _midiRouter;
    private readonly RumbleRouter _rumbleRouter;
    private readonly ILoggerService _loggerService;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="JoystickProfileActivator"/> and
    /// subscribes to <paramref name="events"/>.
    /// </summary>
    /// <param name="events">Source of joystick events.</param>
    /// <param name="autoLoader">Auto-loader used to resolve profiles.</param>
    /// <param name="loaded">Catalogue produced by
    /// <see cref="JoystickProfileAutoLoader.LoadAll"/>.</param>
    /// <param name="midiRouter">Router whose MIDI-on-gameport
    /// settings are reconfigured on connect/disconnect.</param>
    /// <param name="rumbleRouter">Router whose rumble settings are
    /// reconfigured on connect/disconnect.</param>
    /// <param name="loggerService">Logger used for verbose tracing.</param>
    public JoystickProfileActivator(
        IGuiJoystickEvents events,
        JoystickProfileAutoLoader autoLoader,
        LoadedProfiles loaded,
        MidiOnGameportRouter midiRouter,
        RumbleRouter rumbleRouter,
        ILoggerService loggerService) {
        _events = events;
        _autoLoader = autoLoader;
        _loaded = loaded;
        _midiRouter = midiRouter;
        _rumbleRouter = rumbleRouter;
        _loggerService = loggerService;
        _events.JoystickConnectionChanged += OnConnectionChanged;
    }

    /// <summary>
    /// Unsubscribes from the joystick event source.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _events.JoystickConnectionChanged -= OnConnectionChanged;
        _disposed = true;
    }

    private void OnConnectionChanged(object? sender, JoystickConnectionEventArgs e) {
        if (e.IsConnected) {
            JoystickProfile profile = _autoLoader.Resolve(_loaded, string.Empty, e.DeviceName);
            _midiRouter.Configure(profile.MidiOnGameport);
            _rumbleRouter.Configure(profile.Rumble);
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "JOYSTICK: profile activated for stick {Stick} (device='{Device}', profile='{Profile}')",
                    e.StickIndex, e.DeviceName, profile.Name);
            }
        } else {
            _midiRouter.Configure(null);
            _rumbleRouter.Configure(null);
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "JOYSTICK: profile reset for stick {Stick} (disconnected)",
                    e.StickIndex);
            }
        }
    }
}
