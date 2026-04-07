namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Shared.Interfaces;

using System;
using System.Threading;

/// <summary>
/// Monitors the background compilation activity of <see cref="CfgNodeExecutionCompiler"/>,
/// tracking compile times and queue depth.
/// Logs a summary after every <c>logInterval</c> compilations complete.
/// </summary>
public class CfgNodeExecutionCompilerMonitor : IDisposable {
    private const int LogIntervalMs = 500;

    private readonly ILoggerService _logger;
    private readonly Timer? _timer;
    private CompilerState? _lastLoggedState;
    private readonly WindowMetrics _window = new WindowMetrics();

    // Mutable state instance with atomic field updates
    private CompilerState _currentState = new CompilerState();
    private long _queueDepth;

    // Running totals (read via atomic state snapshot)
    /// <summary>Total number of nodes that have received an interpreted delegate.</summary>
    internal long TotalInterpreted => Interlocked.Read(ref _currentState.TotalInterpreted);
    /// <summary>Total number of nodes that have been swapped to an optimized delegate.</summary>
    internal long TotalSwapped => Interlocked.Read(ref _currentState.TotalSwapped);
    /// <summary>Total number of successful compilations.</summary>
    internal long TotalSuccess => Interlocked.Read(ref _currentState.TotalSuccess);
    /// <summary>Total number of failed compilations.</summary>
    internal long TotalFailures => Interlocked.Read(ref _currentState.TotalFailures);
    /// <summary>Current number of items pending in the compilation queue.</summary>
    internal long QueueDepth => Interlocked.Read(ref _queueDepth);

    // Per-window accumulators (stored in _window and guarded by it)

    /// <summary>
    /// Initializes a new instance of <see cref="CfgNodeExecutionCompilerMonitor"/>.
    /// </summary>
    /// <param name="logger">The logger service.</param>
    public CfgNodeExecutionCompilerMonitor(ILoggerService logger) {
        _logger = logger;
        _timer = new Timer(OnTimerTick, null, LogIntervalMs, LogIntervalMs);
    }

    /// <summary>Records that an interpreted delegate was assigned to a node.</summary>
    public void RecordInterpreted() {
        Interlocked.Increment(ref _currentState.TotalInterpreted);
    }

    /// <summary>Records that an optimized delegate was swapped onto a node.</summary>
    public void RecordSwapped() {
        Interlocked.Increment(ref _currentState.TotalSwapped);
    }

    /// <summary>Records that a compilation request was pushed onto the queue.</summary>
    public void RecordQueuePushed() {
        Interlocked.Increment(ref _queueDepth);
    }

    /// <summary>Records that a compilation was completed and the item removed from the queue.</summary>
    public void RecordQueuePopped() {
        Interlocked.Decrement(ref _queueDepth);
    }

    /// <summary>Records a successful fast compilation with the time it took in microseconds.</summary>
    public void RecordCompileSuccess(long microseconds) {
        Interlocked.Increment(ref _currentState.TotalSuccess);

        lock (_window) {
            _window.RecordSuccess(microseconds);
        }
    }

    /// <summary>Records a failed fast compilation with the time it took in microseconds.</summary>
    public void RecordCompileFailure(long microseconds) {
        Interlocked.Increment(ref _currentState.TotalFailures);

        lock (_window) {
            _window.RecordFailure(microseconds);
        }
    }

    // Timer callback: logs the compiled statistics every interval if the state has changed
    // compared to the last logged state. Caller is the thread-pool thread; we lock _window
    // to read and reset the windowed accumulators.
    private void OnTimerTick(object? state) {
        lock (_window) {
            CompilerState current = new CompilerState {
                TotalInterpreted = TotalInterpreted,
                TotalSwapped = TotalSwapped,
                TotalSuccess = TotalSuccess,
                TotalFailures = TotalFailures
            };

            // If nothing changed since last log, skip emitting.
            if (current.Equals(_lastLoggedState)) {
                return;
            }


            if (current.TotalFailures != 0 || _window.FailureCount != 0) {
                _logger.Information(
                    "CfgNodeCompiler: compiled={TotalSuccess}(+{WindowSuccess}), failed={TotalFailures}(+{WindowFailure}), pending={Pending}, queue={Queue}, compileTime[Min={MinMs:F3}ms Avg={AvgMs:F3}ms Max={MaxMs:F3}ms]",
                     current.TotalSuccess, _window.SuccessCount, current.TotalFailures, _window.FailureCount, current.Pending, QueueDepth, _window.MinMs, _window.AvgMs, _window.MaxMs);
            } else {
                _logger.Information(
                    "CfgNodeCompiler: compiled={TotalSuccess}(+{WindowSuccess}), pending={Pending}, queue={Queue}, compileTime[Min={MinMs:F3}ms Avg={AvgMs:F3}ms Max={MaxMs:F3}ms]",
                     current.TotalSuccess, _window.SuccessCount, current.Pending, QueueDepth, _window.MinMs, _window.AvgMs, _window.MaxMs);
            }

            _lastLoggedState = current;
            _window.Reset();
        }
    }

    private sealed class WindowMetrics {
        public long SuccessCount;
        public long FailureCount;
        private long _microseconds;
        private long _minMicroseconds = long.MaxValue;
        private long _maxMicroseconds;

        private long CompileCount => SuccessCount + FailureCount;

        public double AvgMs => CompileCount > 0 ? (double)_microseconds / CompileCount / 1000.0 : 0.0;

        public double MinMs => _minMicroseconds == long.MaxValue ? 0.0 : _minMicroseconds / 1000.0;

        public double MaxMs => CompileCount > 0 ? _maxMicroseconds / 1000.0 : 0.0;

        public void RecordSuccess(long microseconds) {
            SuccessCount++;
            UpdateTime(microseconds);
        }

        public void RecordFailure(long microseconds) {
            FailureCount++;
            UpdateTime(microseconds);
        }

        private void UpdateTime(long microseconds) {
            _microseconds += microseconds;
            _minMicroseconds = Math.Min(_minMicroseconds, microseconds);
            _maxMicroseconds = Math.Max(_maxMicroseconds, microseconds);
        }

        public void Reset() {
            SuccessCount = 0;
            FailureCount = 0;
            _microseconds = 0;
            _minMicroseconds = long.MaxValue;
            _maxMicroseconds = 0;
        }
    }

    private record CompilerState {
        public long TotalInterpreted;
        public long TotalSwapped;
        public long TotalSuccess;
        public long TotalFailures;
        public long Pending => TotalInterpreted - TotalSwapped;
    }

    public void Dispose() {
        _timer?.Dispose();
    }
}
