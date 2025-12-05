namespace Spice86.Core.Emulator.Devices.ExternalInput;


using Spice86.Shared.Interfaces;

public class EmulationLoopSchedulerMonitor {
    private readonly ILoggerService _logger;
    private readonly long _logInterval;
    private readonly int _maxQueueSizeThreshold;
    private readonly double _maxLagThreshold;
    
    private long _currentWindowCount;
    private double _currentWindowTotalLag;
    private double _currentWindowMaxLag;
    private double _currentWindowMinLag = double.MaxValue;

    private long _currentWindowTotalQueueSize;
    private int _currentWindowMaxQueueSize;
    private int _currentWindowMinQueueSize = int.MaxValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulationLoopSchedulerMonitor"/> class.
    /// </summary>
    /// <param name="logger">The logger service.</param>
    /// <param name="logInterval">The number of events to process before logging statistics.</param>
    /// <param name="maxQueueSizeThreshold">The maximum queue size before a warning is logged.</param>
    /// <param name="maxLagThreshold">The maximum lag in milliseconds before a warning is logged.</param>
    public EmulationLoopSchedulerMonitor(ILoggerService logger, long logInterval = 5000, int maxQueueSizeThreshold = 100, double maxLagThreshold = 10) {
        _logger = logger;
        _logInterval = logInterval;
        _maxQueueSizeThreshold = maxQueueSizeThreshold;
        _maxLagThreshold = maxLagThreshold;
    }

    /// <summary>
    /// Records the execution of an event and logs statistics if the interval is reached.
    /// </summary>
    /// <param name="scheduledTime">The time the event was scheduled for.</param>
    /// <param name="actualTime">The time the event was actually executed.</param>
    /// <param name="queueSize">The current size of the event queue.</param>
    public void OnEventExecuted(double scheduledTime, double actualTime, int queueSize) {
        double lag = actualTime - scheduledTime;
        
        _currentWindowCount++;
        _currentWindowTotalLag += lag;
        _currentWindowTotalQueueSize += queueSize;
        
        if (lag > _currentWindowMaxLag) {
            _currentWindowMaxLag = lag;
        }
        if (lag < _currentWindowMinLag) {
            _currentWindowMinLag = lag;
        }

        if (queueSize > _currentWindowMaxQueueSize) {
            _currentWindowMaxQueueSize = queueSize;
        }
        if (queueSize < _currentWindowMinQueueSize) {
            _currentWindowMinQueueSize = queueSize;
        }

        if (_currentWindowCount >= _logInterval) {
            if (_currentWindowMaxQueueSize > _maxQueueSizeThreshold || _currentWindowMaxLag > _maxLagThreshold) {
                double avgLag = _currentWindowTotalLag / _currentWindowCount;
                double avgQueueSize = (double)_currentWindowTotalQueueSize / _currentWindowCount;

                _logger.Warning("Scheduler Monitor: Lag[Min={MinLag:F4}ms Avg={AvgLag:F4}ms Max={MaxLag:F4}ms] Queue[Min={MinQueue} Avg={AvgQueue:F2} Max={MaxQueue}]", 
                    _currentWindowMinLag, avgLag, _currentWindowMaxLag, 
                    _currentWindowMinQueueSize, avgQueueSize, _currentWindowMaxQueueSize);
            }
            
            ResetWindow();
        }
    }

    private void ResetWindow() {
        _currentWindowCount = 0;
        _currentWindowTotalLag = 0;
        _currentWindowMaxLag = 0;
        _currentWindowMinLag = double.MaxValue;

        _currentWindowTotalQueueSize = 0;
        _currentWindowMaxQueueSize = 0;
        _currentWindowMinQueueSize = int.MaxValue;
    }
}
