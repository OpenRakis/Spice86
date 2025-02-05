namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Diagnostics;

using System.Threading;


/// <summary>
/// This class is used to limit the number of cycles that can be executed in a wall clock millisecond. <br/>
/// We use this for time sensitive games that rely on the CPU being slow. <br/>
/// For real mode games, 3000 cycles/ms is fine the vast majority of the time. <br/>
/// We use a timer to calculate the number of cycles executed in the last millisecond and we subtract that from the target. <br/>
/// For simplicity, and since we don't care about cycle accurate CPU instructions, 1 CPU cycle = 1 millisecond. <br/>
/// </summary>
public class CyclesLimiter : IDisposable {
    public const int RealModeCpuCyclesTarget = 3000;
    private const int MinimumCpuCyclesTarget = 50;
    private const int MaximumCpuCyclesTarget = 60000;
    private const int CycleUp = 1000;
    private const int CycleDown = 1000;

    private readonly State _state;
    // A System.Threading.Timer runs a callback on a thread pool thread at regular intervals.
    private readonly Timer _timer;
    private bool disposedValue;
    private bool _mustMakeTheCpuWait;
    private readonly PerformanceMeasurer _performanceMeasurer = new();
    private readonly IPauseHandler _pauseHandler;

    public int TargetCpuCylesPerMs { get; set; } = RealModeCpuCyclesTarget;

    public void IncreaseCycles() => TargetCpuCylesPerMs = Math.Min(TargetCpuCylesPerMs + CycleUp, MaximumCpuCyclesTarget);

    public void DecreaseCycles() => TargetCpuCylesPerMs = Math.Max(TargetCpuCylesPerMs - CycleDown, MinimumCpuCyclesTarget);

    public CyclesLimiter(State state, IPauseHandler pauseHandler) {
        _pauseHandler = pauseHandler;
        _state = state;
        _timer = new Timer((_) => CaculateCpuCyclesLeft(), state: null,
            dueTime: 0, period: 1);
    }

    private void CaculateCpuCyclesLeft() {
        if(!_state.IsRunning ||
            _pauseHandler.IsPaused) {
            return;
        }

        _performanceMeasurer.UpdateValue(_state.Cycles);
        _mustMakeTheCpuWait = _performanceMeasurer.ValuePerMillisecond > TargetCpuCylesPerMs;
    }

    internal void Wait() {
        if(!_mustMakeTheCpuWait || 
            _pauseHandler.IsPaused ||
            Thread.CurrentThread.Name != "Emulator") {
            return;
        }
        Thread.Sleep(1);
    }

    private void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                _timer.Dispose();
            }
            disposedValue = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
