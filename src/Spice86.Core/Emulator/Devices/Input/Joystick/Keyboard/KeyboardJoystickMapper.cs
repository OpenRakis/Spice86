namespace Spice86.Core.Emulator.Devices.Input.Joystick.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// Hardware-free joystick fallback: turns host keyboard events
/// into virtual joystick axis and button events.
/// </summary>
/// <remarks>
/// <para>This mirrors DOSBox-Staging's "keyboard joystick"
/// fallback. When no real controller is connected, users can still
/// play DOS games that require a joystick by binding axis halves
/// and buttons to physical keys.</para>
/// <para>The mapper subscribes to
/// <see cref="InputEventHub.KeyDown"/> and
/// <see cref="InputEventHub.KeyUp"/>, tracks which axis halves are
/// currently pressed, and re-posts the resulting joystick events
/// back to the same hub via
/// <see cref="InputEventHub.PostJoystickAxisEvent"/> and
/// <see cref="InputEventHub.PostJoystickButtonEvent"/>. Two
/// bindings on the same axis with opposite
/// <see cref="KeyboardJoystickBinding.AxisSign"/> values combine
/// into a tri-state axis (-1 / 0 / +1); the axis re-centres to
/// <c>0</c> as soon as both halves are released or are pressed
/// simultaneously.</para>
/// <para>The mapper is gated by <see cref="Enabled"/>. While
/// disabled, key events are ignored, but any axis already off
/// centre is re-centred to <c>0</c> on the next disable transition
/// so games never see a stuck stick.</para>
/// </remarks>
public sealed class KeyboardJoystickMapper : IDisposable {
    private const string LogPrefix = "JOYSTICK: keyboard mapper";

    private readonly InputEventHub _hub;
    private readonly ILoggerService _logger;
    private readonly Dictionary<PhysicalKey, KeyboardJoystickBinding> _bindings;
    private readonly Dictionary<JoystickAxis, AxisHalfState> _axisState;
    private bool _enabled;
    private bool _disposed;

    /// <summary>Initializes a new mapper subscribed to
    /// <paramref name="hub"/>'s key events. Starts disabled with
    /// the DOSBox-style default bindings (arrows -> X/Y axis,
    /// Space -> button 0, Enter -> button 1).</summary>
    /// <param name="hub">Hub the mapper listens on and posts back
    /// to.</param>
    /// <param name="logger">Logger for transition diagnostics.</param>
    public KeyboardJoystickMapper(InputEventHub hub, ILoggerService logger) {
        _hub = hub;
        _logger = logger;
        _bindings = BuildDefaultBindings();
        _axisState = new Dictionary<JoystickAxis, AxisHalfState>();
        _hub.KeyDown += OnKeyDown;
        _hub.KeyUp += OnKeyUp;
    }

    /// <summary>Stick index the mapper drives. Defaults to
    /// <c>0</c>.</summary>
    public int StickIndex { get; set; }

    /// <summary>When <see langword="false"/>, key events are
    /// ignored and any non-centred axis is re-centred. Defaults to
    /// <see langword="false"/>.</summary>
    public bool Enabled {
        get { return _enabled; }
        set {
            if (_enabled == value) {
                return;
            }
            _enabled = value;
            if (!_enabled) {
                RecentreAllAxes();
            }
            if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                _logger.Verbose("{Prefix}: {State} for stick {Stick}",
                    LogPrefix, _enabled ? "enabled" : "disabled",
                    StickIndex);
            }
        }
    }

    /// <summary>Replaces all bindings. Pass an empty dictionary to
    /// disable every key. Re-centres any axis whose halves are no
    /// longer mapped.</summary>
    /// <param name="bindings">Replacement key-to-binding map.</param>
    public void SetBindings(IReadOnlyDictionary<PhysicalKey, KeyboardJoystickBinding> bindings) {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings.Clear();
        foreach (KeyValuePair<PhysicalKey, KeyboardJoystickBinding> kvp in bindings) {
            _bindings[kvp.Key] = kvp.Value;
        }
        RecentreAllAxes();
    }

    /// <summary>Default DOSBox-style bindings: arrows drive the X
    /// and Y axes, Space and Enter drive buttons 0 and 1.</summary>
    /// <returns>A fresh, mutable dictionary of default bindings.</returns>
    public static Dictionary<PhysicalKey, KeyboardJoystickBinding> BuildDefaultBindings() {
        return new Dictionary<PhysicalKey, KeyboardJoystickBinding> {
            [PhysicalKey.ArrowLeft] = KeyboardJoystickBinding.ForAxis(JoystickAxis.X, -1),
            [PhysicalKey.ArrowRight] = KeyboardJoystickBinding.ForAxis(JoystickAxis.X, +1),
            [PhysicalKey.ArrowUp] = KeyboardJoystickBinding.ForAxis(JoystickAxis.Y, -1),
            [PhysicalKey.ArrowDown] = KeyboardJoystickBinding.ForAxis(JoystickAxis.Y, +1),
            [PhysicalKey.Space] = KeyboardJoystickBinding.ForButton(0),
            [PhysicalKey.Enter] = KeyboardJoystickBinding.ForButton(1),
        };
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _hub.KeyDown -= OnKeyDown;
        _hub.KeyUp -= OnKeyUp;
    }

    private void OnKeyDown(object? sender, KeyboardEventArgs e) {
        HandleKey(e.Key, true);
    }

    private void OnKeyUp(object? sender, KeyboardEventArgs e) {
        HandleKey(e.Key, false);
    }

    private void HandleKey(PhysicalKey key, bool pressed) {
        if (_disposed || !_enabled) {
            return;
        }
        if (!_bindings.TryGetValue(key, out KeyboardJoystickBinding? binding)) {
            return;
        }
        if (binding.Kind == KeyboardJoystickBindingKind.Button) {
            _hub.PostJoystickButtonEvent(new JoystickButtonEventArgs(
                StickIndex, binding.ButtonIndex, pressed));
            return;
        }
        if (binding.Kind == KeyboardJoystickBindingKind.AxisDirection) {
            UpdateAxisHalf(binding.Axis, binding.AxisSign, pressed);
        }
    }

    private void UpdateAxisHalf(JoystickAxis axis, int sign, bool pressed) {
        AxisHalfState state = _axisState.GetValueOrDefault(axis);
        if (sign < 0) {
            state.NegativePressed = pressed;
        } else if (sign > 0) {
            state.PositivePressed = pressed;
        } else {
            return;
        }
        float newValue = ComputeAxisValue(state);
        if (newValue == state.LastPostedValue) {
            _axisState[axis] = state;
            return;
        }
        state.LastPostedValue = newValue;
        _axisState[axis] = state;
        _hub.PostJoystickAxisEvent(new JoystickAxisEventArgs(
            StickIndex, axis, newValue));
    }

    private static float ComputeAxisValue(AxisHalfState state) {
        int magnitude = 0;
        if (state.PositivePressed) {
            magnitude += 1;
        }
        if (state.NegativePressed) {
            magnitude -= 1;
        }
        return magnitude;
    }

    private void RecentreAllAxes() {
        JoystickAxis[] axes = new JoystickAxis[_axisState.Count];
        _axisState.Keys.CopyTo(axes, 0);
        foreach (JoystickAxis axis in axes) {
            AxisHalfState state = _axisState[axis];
            if (state.LastPostedValue == 0f) {
                continue;
            }
            state.NegativePressed = false;
            state.PositivePressed = false;
            state.LastPostedValue = 0f;
            _axisState[axis] = state;
            _hub.PostJoystickAxisEvent(new JoystickAxisEventArgs(
                StickIndex, axis, 0f));
        }
    }

    private struct AxisHalfState {
        public bool NegativePressed;
        public bool PositivePressed;
        public float LastPostedValue;
    }
}
