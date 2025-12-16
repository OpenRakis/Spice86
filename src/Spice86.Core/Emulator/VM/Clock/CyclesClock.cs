namespace Spice86.Core.Emulator.VM.Clock;

using Spice86.Core.Emulator.CPU;

public class CyclesClock(State cpuState, long cyclesPerSecond) : IEmulatedClock {
    private DateTime _pauseStartTime;
    private TimeSpan _totalPausedTime = TimeSpan.Zero;
    private bool _isPaused;

    public long CyclesPerSecond { get; set; } = cyclesPerSecond;

    public double CurrentTimeMs {
        get {
            double elapsedMs = (double)cpuState.Cycles * 1000 / CyclesPerSecond;
            double pausedMs = _totalPausedTime.TotalMilliseconds;
            
            if (_isPaused) {
                pausedMs += (DateTime.UtcNow - _pauseStartTime).TotalMilliseconds;
            }
            
            return Math.Max(0, elapsedMs - pausedMs);
        }
    }

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime CurrentDateTime => StartTime.AddMilliseconds(CurrentTimeMs);

    public void OnPause() {
        if (_isPaused) {
            return;
        }
        _pauseStartTime = DateTime.UtcNow;
        _isPaused = true;
    }

    public void OnResume() {
        if (!_isPaused) {
            return;
        }
        _totalPausedTime += DateTime.UtcNow - _pauseStartTime;
        _isPaused = false;
    }
}