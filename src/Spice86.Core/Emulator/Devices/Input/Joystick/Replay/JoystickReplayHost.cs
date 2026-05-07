namespace Spice86.Core.Emulator.Devices.Input.Joystick.Replay;

using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Real-time host that drives a <see cref="JoystickReplayPlayer"/>
/// off an <see cref="ITimeProvider"/>. The emulator main loop, an
/// MCP scripted-input tool or the mapper UI's "test profile"
/// button calls <see cref="Tick"/> every frame; the host computes
/// monotonic milliseconds since <see cref="Start"/> was called and
/// forwards them to <see cref="JoystickReplayPlayer.AdvanceTo"/>.
/// </summary>
/// <remarks>
/// Pull-based, single-threaded by design (no async, no Task, no
/// background thread) — keeps async out of <c>Spice86.Core</c> and
/// makes the host trivially testable with a fake
/// <see cref="ITimeProvider"/>. Mirrors the DOSBox-Staging
/// "playback" loop in <c>autoexec.cpp</c> where the scripted
/// command stream is pumped from the same tick the emulator runs
/// on.
/// </remarks>
public sealed class JoystickReplayHost {
    private readonly JoystickReplayPlayer _player;
    private readonly ITimeProvider _timeProvider;
    private DateTime _startTime;
    private bool _running;

    /// <summary>
    /// Initializes a new <see cref="JoystickReplayHost"/>.
    /// </summary>
    /// <param name="player">Player whose
    /// <see cref="JoystickReplayPlayer.AdvanceTo"/> is invoked each
    /// tick.</param>
    /// <param name="timeProvider">Source of monotonic time. The
    /// host only takes <see cref="ITimeProvider.Now"/> deltas, so
    /// any clock that is non-decreasing across <see cref="Tick"/>
    /// calls is acceptable.</param>
    public JoystickReplayHost(JoystickReplayPlayer player, ITimeProvider timeProvider) {
        _player = player;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Player driven by this host. Useful for callers that want to
    /// inspect <see cref="JoystickReplayPlayer.IsFinished"/> or
    /// stop early on a step count.
    /// </summary>
    public JoystickReplayPlayer Player => _player;

    /// <summary>
    /// True after <see cref="Start"/> until <see cref="Stop"/> (or
    /// the player finishes and is stopped automatically by
    /// <see cref="Tick"/>).
    /// </summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Marks "now" as t=0 for the script and arms the host. A
    /// second <see cref="Start"/> call resets the start time so the
    /// same player can be replayed from the beginning if it has
    /// been seeked back externally; the player itself is not reset
    /// here because rewinding it requires constructing a new one.
    /// </summary>
    public void Start() {
        _startTime = _timeProvider.Now;
        _running = true;
    }

    /// <summary>
    /// Disarms the host. Subsequent <see cref="Tick"/> calls become
    /// no-ops until <see cref="Start"/> is called again.
    /// </summary>
    public void Stop() {
        _running = false;
    }

    /// <summary>
    /// Pumps any due steps. Returns the number of steps posted by
    /// this tick. When the player has played the last step, the
    /// host stops itself so the host loop can detect completion via
    /// <see cref="IsRunning"/> without polling
    /// <see cref="JoystickReplayPlayer.IsFinished"/>.
    /// </summary>
    /// <returns>Number of steps posted.</returns>
    public int Tick() {
        if (!_running) {
            return 0;
        }
        TimeSpan elapsed = _timeProvider.Now - _startTime;
        double elapsedMs = elapsed.TotalMilliseconds;
        if (elapsedMs < 0.0) {
            elapsedMs = 0.0;
        }
        int posted = _player.AdvanceTo(elapsedMs);
        if (_player.IsFinished) {
            _running = false;
        }
        return posted;
    }
}
