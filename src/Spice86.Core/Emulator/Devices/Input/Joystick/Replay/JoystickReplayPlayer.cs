namespace Spice86.Core.Emulator.Devices.Input.Joystick.Replay;

using Serilog.Events;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Replay;
using Spice86.Shared.Interfaces;

/// <summary>
/// Pull-based player that walks a <see cref="JoystickReplayScript"/>
/// in monotonic time and posts every due step to the
/// <see cref="InputEventHub"/> via its
/// <c>PostJoystick*Event</c> methods.
/// </summary>
/// <remarks>
/// The player is deterministic and does not own a clock or thread:
/// the host calls <see cref="AdvanceTo(double)"/> with the current
/// elapsed milliseconds since the start of the script, and every
/// step whose cumulative deadline has been reached is posted. This
/// makes it trivially testable with a fake clock and equally easy
/// to drive from a real timer thread, the MCP server or a headless
/// test harness. Steps with payload fields outside the supported
/// ranges are skipped with a warning instead of crashing the run.
/// </remarks>
public sealed class JoystickReplayPlayer {
    private readonly InputEventHub _hub;
    private readonly JoystickReplayScript _script;
    private readonly ILoggerService _loggerService;
    private readonly double[] _stepDeadlinesMs;
    private int _nextStepIndex;

    /// <summary>
    /// Initializes a new <see cref="JoystickReplayPlayer"/>.
    /// </summary>
    /// <param name="hub">Hub the events are posted to. The hub
    /// queues each event so it surfaces on the emulator thread on
    /// the next <see cref="InputEventHub.ProcessAllPendingInputEvents"/>
    /// pump.</param>
    /// <param name="script">Pre-parsed script. Steps are processed
    /// in order; each <see cref="JoystickReplayStep.DelayMs"/> is
    /// added to the previous deadline.</param>
    /// <param name="loggerService">Logger used for the start /
    /// completion / skipped-step messages.</param>
    public JoystickReplayPlayer(
        InputEventHub hub,
        JoystickReplayScript script,
        ILoggerService loggerService) {
        _hub = hub;
        _script = script;
        _loggerService = loggerService;
        _stepDeadlinesMs = ComputeDeadlines(script);
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "JOYSTICK: replay '{Name}' loaded with {Count} step(s)",
                script.Name, script.Steps.Count);
        }
    }

    /// <summary>
    /// Friendly script name, exposed for diagnostics and the mapper
    /// UI.
    /// </summary>
    public string Name => _script.Name;

    /// <summary>
    /// Total number of steps in the script.
    /// </summary>
    public int StepCount => _script.Steps.Count;

    /// <summary>
    /// Index of the next step that has not yet been posted. When
    /// equal to <see cref="StepCount"/>, the player has played the
    /// whole script.
    /// </summary>
    public int NextStepIndex => _nextStepIndex;

    /// <summary>
    /// True when every step has been posted.
    /// </summary>
    public bool IsFinished => _nextStepIndex >= _script.Steps.Count;

    /// <summary>
    /// Deadline (cumulative ms from script start) of the last step.
    /// Useful for hosts that want to know when the script is done.
    /// </summary>
    public double TotalDurationMs {
        get {
            if (_stepDeadlinesMs.Length == 0) {
                return 0.0;
            }
            return _stepDeadlinesMs[_stepDeadlinesMs.Length - 1];
        }
    }

    /// <summary>
    /// Advances the player to the given monotonic timestamp. Every
    /// pending step whose cumulative deadline is at or before
    /// <paramref name="elapsedMs"/> is posted to the hub, in order.
    /// Returns the number of steps posted by this call.
    /// </summary>
    /// <param name="elapsedMs">Milliseconds elapsed since the start
    /// of the script. Must be monotonic across calls.</param>
    /// <returns>How many steps were posted by this call.</returns>
    public int AdvanceTo(double elapsedMs) {
        int posted = 0;
        while (_nextStepIndex < _script.Steps.Count
               && _stepDeadlinesMs[_nextStepIndex] <= elapsedMs) {
            JoystickReplayStep step = _script.Steps[_nextStepIndex];
            if (PostStep(step)) {
                posted++;
            }
            _nextStepIndex++;
        }
        if (posted > 0 && IsFinished
            && _loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "JOYSTICK: replay '{Name}' finished after {Steps} step(s)",
                _script.Name, _script.Steps.Count);
        }
        return posted;
    }

    private bool PostStep(JoystickReplayStep step) {
        if (step.Type == JoystickReplayStepType.Axis) {
            _hub.PostJoystickAxisEvent(
                new JoystickAxisEventArgs(step.StickIndex, step.Axis, step.Value));
            return true;
        }
        if (step.Type == JoystickReplayStepType.Button) {
            if (step.ButtonIndex < 0 || step.ButtonIndex > 3) {
                LogSkipped(step, "button index out of range (0..3)");
                return false;
            }
            _hub.PostJoystickButtonEvent(
                new JoystickButtonEventArgs(step.StickIndex, step.ButtonIndex, step.Pressed));
            return true;
        }
        if (step.Type == JoystickReplayStepType.Hat) {
            _hub.PostJoystickHatEvent(
                new JoystickHatEventArgs(step.StickIndex, step.Direction));
            return true;
        }
        if (step.Type == JoystickReplayStepType.Connect) {
            _hub.PostJoystickConnectionEvent(
                new JoystickConnectionEventArgs(step.StickIndex, true, step.DeviceName));
            return true;
        }
        if (step.Type == JoystickReplayStepType.Disconnect) {
            _hub.PostJoystickConnectionEvent(
                new JoystickConnectionEventArgs(step.StickIndex, false, string.Empty));
            return true;
        }
        LogSkipped(step, "unknown step type");
        return false;
    }

    private void LogSkipped(JoystickReplayStep step, string reason) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning(
                "JOYSTICK: skipping replay step (type={Type}, stick={Stick}): {Reason}",
                step.Type, step.StickIndex, reason);
        }
    }

    private static double[] ComputeDeadlines(JoystickReplayScript script) {
        double[] deadlines = new double[script.Steps.Count];
        double cumulativeMs = 0.0;
        for (int i = 0; i < script.Steps.Count; i++) {
            double delay = script.Steps[i].DelayMs;
            if (delay < 0.0) {
                delay = 0.0;
            }
            cumulativeMs += delay;
            deadlines[i] = cumulativeMs;
        }
        return deadlines;
    }
}
