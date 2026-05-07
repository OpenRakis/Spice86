namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// RC-decay timer for the four gameport axis one-shots, matching the
/// timed read/write path of DOSBox Staging
/// (<c>read_p201_timed</c>/<c>write_p201_timed</c> in
/// <c>src/hardware/input/joystick.cpp</c>).
/// </summary>
/// <remarks>
/// Each axis on the IBM gameport is wired through a 558 monostable
/// one-shot whose pulse duration is proportional to the stick's
/// potentiometer position. A write to port <c>0x201</c> arms all
/// four one-shots; subsequent reads of port <c>0x201</c> return
/// <c>0</c> in the corresponding bit until the deadline computed at
/// arm-time is reached.
/// <para>
/// The deadline formula is
/// <c>now + (position + 1) * scalar + offset</c>, where
/// <c>scalar</c> and <c>offset</c> are the calibrated values defined
/// in <see cref="GameportConstants"/>.
/// </para>
/// </remarks>
public sealed class GameportTimer {
    private double _stickAxDeadlineMs;
    private double _stickAyDeadlineMs;
    private double _stickBxDeadlineMs;
    private double _stickByDeadlineMs;
    private double _lastArmedMs;
    private bool _armed;

    /// <summary>
    /// Calibration scalar applied to X axes when computing pulse
    /// deadlines. Defaults to
    /// <see cref="GameportConstants.DefaultAxisXScalar"/>.
    /// </summary>
    public double XScalar { get; set; } = GameportConstants.DefaultAxisXScalar;

    /// <summary>
    /// Calibration scalar applied to Y axes when computing pulse
    /// deadlines. Defaults to
    /// <see cref="GameportConstants.DefaultAxisYScalar"/>.
    /// </summary>
    public double YScalar { get; set; } = GameportConstants.DefaultAxisYScalar;

    /// <summary>
    /// Constant offset (in milliseconds) added to every axis
    /// deadline. Defaults to
    /// <see cref="GameportConstants.DefaultAxisOffsetMs"/>.
    /// </summary>
    public double OffsetMs { get; set; } = GameportConstants.DefaultAxisOffsetMs;

    /// <summary>
    /// Whether the timer is currently in the "armed" window (i.e.
    /// the last write to port <c>0x201</c> was less than
    /// <see cref="GameportConstants.LegacyResetTimeoutTicks"/>
    /// milliseconds ago). When the window expires all axis bits
    /// become inactive and stay so until the next write.
    /// </summary>
    /// <param name="nowMs">Current absolute time in milliseconds.</param>
    /// <returns><see langword="true"/> while inside the armed window.</returns>
    public bool IsInsideArmedWindow(double nowMs) {
        if (!_armed) {
            return false;
        }
        return (nowMs - _lastArmedMs) <= GameportConstants.LegacyResetTimeoutTicks;
    }

    /// <summary>
    /// Arms all four axis one-shots for the given joystick state at
    /// the given absolute time. Equivalent to DOSBox Staging's
    /// <c>write_p201_timed</c>.
    /// </summary>
    /// <param name="state">Current input state.</param>
    /// <param name="nowMs">Current absolute time in milliseconds.</param>
    public void Arm(VirtualJoystickState state, double nowMs) {
        _armed = true;
        _lastArmedMs = nowMs;
        _stickAxDeadlineMs = ComputeDeadline(state.StickA.X, XScalar, nowMs);
        _stickAyDeadlineMs = ComputeDeadline(state.StickA.Y, YScalar, nowMs);
        _stickBxDeadlineMs = ComputeDeadline(state.StickB.X, XScalar, nowMs);
        _stickByDeadlineMs = ComputeDeadline(state.StickB.Y, YScalar, nowMs);
    }

    /// <summary>
    /// Forces the timer back to its disarmed state. After this call
    /// every axis bit reports inactive until the next
    /// <see cref="Arm(VirtualJoystickState, double)"/>.
    /// </summary>
    public void Disarm() {
        _armed = false;
        _stickAxDeadlineMs = 0;
        _stickAyDeadlineMs = 0;
        _stickBxDeadlineMs = 0;
        _stickByDeadlineMs = 0;
    }

    /// <summary>
    /// Returns whether stick A's X axis one-shot is still firing at
    /// the given absolute time.
    /// </summary>
    /// <param name="nowMs">Current absolute time in milliseconds.</param>
    public bool IsStickAxActive(double nowMs) {
        return _armed && nowMs < _stickAxDeadlineMs;
    }

    /// <summary>Same as <see cref="IsStickAxActive(double)"/> for stick A Y axis.</summary>
    /// <param name="nowMs">Current absolute time in milliseconds.</param>
    public bool IsStickAyActive(double nowMs) {
        return _armed && nowMs < _stickAyDeadlineMs;
    }

    /// <summary>Same as <see cref="IsStickAxActive(double)"/> for stick B X axis.</summary>
    /// <param name="nowMs">Current absolute time in milliseconds.</param>
    public bool IsStickBxActive(double nowMs) {
        return _armed && nowMs < _stickBxDeadlineMs;
    }

    /// <summary>Same as <see cref="IsStickAxActive(double)"/> for stick B Y axis.</summary>
    /// <param name="nowMs">Current absolute time in milliseconds.</param>
    public bool IsStickByActive(double nowMs) {
        return _armed && nowMs < _stickByDeadlineMs;
    }

    private double ComputeDeadline(float axisValue, double scalar, double nowMs) {
        return nowMs + (axisValue + 1.0) * scalar + OffsetMs;
    }
}
