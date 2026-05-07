namespace Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;

using Serilog.Events;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Routes game-triggered rumble effects to an
/// <see cref="IRumbleSink"/>, applying the per-profile
/// <see cref="RumbleMapping"/> (enable flag and global amplitude
/// scale).
/// </summary>
/// <remarks>
/// This sits between the emulator (overrides, DOS games using
/// joystick force-feedback APIs, future scripted input) and the
/// UI-side haptic backend. It mirrors DOSBox Staging's rumble
/// forwarding: when the active profile disables rumble, requests
/// are silently dropped (still logged at verbose level so the UI
/// can surface "rumble suppressed"); when enabled, motor
/// amplitudes are clamped to <c>[0, 1]</c> and multiplied by
/// <see cref="RumbleMapping.AmplitudeScale"/> before forwarding.
/// A null sink, or a sink that does not advertise rumble support,
/// turns every call into a no-op.
/// </remarks>
public sealed class RumbleRouter {
    private readonly IRumbleSink? _sink;
    private readonly ILoggerService _loggerService;
    private readonly object _lock = new();

    private bool _enabled = true;
    private double _amplitudeScale = 1.0;

    /// <summary>
    /// Initializes a new <see cref="RumbleRouter"/>.
    /// </summary>
    /// <param name="sink">Underlying haptic sink, or
    /// <see langword="null"/> when no haptic device is wired
    /// (headless / tests). A non-null sink whose
    /// <see cref="IRumbleSink.IsSupported"/> is <see langword="false"/>
    /// is treated like no sink at all.</param>
    /// <param name="loggerService">Logger used for the
    /// enable/disable transition messages and the verbose
    /// "rumble suppressed" trace.</param>
    public RumbleRouter(IRumbleSink? sink, ILoggerService loggerService) {
        _sink = sink;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Whether rumble forwarding is currently enabled (per the
    /// active <see cref="RumbleMapping"/>).
    /// </summary>
    public bool IsEnabled {
        get {
            lock (_lock) {
                return _enabled;
            }
        }
    }

    /// <summary>
    /// Active amplitude scale in <c>[0, 1]</c>.
    /// </summary>
    public double AmplitudeScale {
        get {
            lock (_lock) {
                return _amplitudeScale;
            }
        }
    }

    /// <summary>
    /// Whether the wired sink advertises haptic support. Useful for
    /// the UI to dim "rumble" toggles when no haptic-capable device
    /// is connected.
    /// </summary>
    public bool IsSinkSupported => _sink is not null && _sink.IsSupported;

    /// <summary>
    /// Applies the rumble section of a joystick profile.
    /// A <see langword="null"/> argument restores defaults
    /// (enabled, scale 1.0).
    /// </summary>
    /// <param name="mapping">Rumble settings to apply, or
    /// <see langword="null"/> to restore defaults.</param>
    public void Configure(RumbleMapping? mapping) {
        bool wasEnabled;
        bool isEnabled;
        double scale;
        lock (_lock) {
            wasEnabled = _enabled;
            if (mapping is null) {
                _enabled = true;
                _amplitudeScale = 1.0;
            } else {
                _enabled = mapping.Enabled;
                _amplitudeScale = ClampUnit(mapping.AmplitudeScale);
            }
            isEnabled = _enabled;
            scale = _amplitudeScale;
        }
        if (wasEnabled == isEnabled) {
            return;
        }
        if (isEnabled) {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information(
                    "JOYSTICK: rumble enabled (scale={Scale:0.00})", scale);
            }
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("JOYSTICK: rumble disabled");
            }
        }
    }

    /// <summary>
    /// Forwards an effect to the sink, scaled by the active
    /// <see cref="AmplitudeScale"/>. Negative amplitudes are
    /// clamped to <c>0</c>; values above <c>1</c> are clamped to
    /// <c>1</c>. A negative duration is treated as <c>0</c> (which
    /// stops any active effect, matching SDL semantics).
    /// </summary>
    /// <param name="stickIndex">Zero-based stick slot (0 or 1).</param>
    /// <param name="effect">Effect to play.</param>
    /// <returns><see langword="true"/> when the effect was
    /// forwarded; <see langword="false"/> when suppressed (router
    /// disabled, no sink, or sink reports no haptic support).</returns>
    public bool Play(int stickIndex, RumbleEffect effect) {
        if (stickIndex < 0 || stickIndex > 1) {
            throw new ArgumentOutOfRangeException(nameof(stickIndex), stickIndex,
                "Stick index must be 0 or 1.");
        }
        bool enabled;
        double scale;
        lock (_lock) {
            enabled = _enabled;
            scale = _amplitudeScale;
        }
        if (_sink is null || !_sink.IsSupported) {
            return false;
        }
        if (!enabled) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "JOYSTICK: rumble suppressed (stick={Stick}, lf={Lf:0.00}, hf={Hf:0.00}, dur={Dur}ms)",
                    stickIndex,
                    effect.LowFrequencyAmplitude,
                    effect.HighFrequencyAmplitude,
                    effect.DurationMilliseconds);
            }
            return false;
        }
        RumbleEffect scaled = new(
            (float)ClampUnit(effect.LowFrequencyAmplitude * scale),
            (float)ClampUnit(effect.HighFrequencyAmplitude * scale),
            Math.Max(0, effect.DurationMilliseconds));
        _sink.Play(stickIndex, scaled);
        return true;
    }

    /// <summary>
    /// Stops any active effect on the given stick. Always
    /// forwarded when a supported sink is wired, even when the
    /// router is disabled — disabling rumble must not leave a motor
    /// running.
    /// </summary>
    /// <param name="stickIndex">Zero-based stick slot (0 or 1).</param>
    /// <returns><see langword="true"/> when the stop was forwarded;
    /// <see langword="false"/> when no supported sink is wired.</returns>
    public bool Stop(int stickIndex) {
        if (stickIndex < 0 || stickIndex > 1) {
            throw new ArgumentOutOfRangeException(nameof(stickIndex), stickIndex,
                "Stick index must be 0 or 1.");
        }
        if (_sink is null || !_sink.IsSupported) {
            return false;
        }
        _sink.Play(stickIndex, RumbleEffect.Stop);
        return true;
    }

    private static float ClampUnit(float value) {
        if (value < 0f) {
            return 0f;
        }
        if (value > 1f) {
            return 1f;
        }
        return value;
    }

    private static double ClampUnit(double value) {
        if (value < 0.0) {
            return 0.0;
        }
        if (value > 1.0) {
            return 1.0;
        }
        return value;
    }
}
