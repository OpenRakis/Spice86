namespace Spice86.Shared.Diagnostics;

using Spice86.Shared.Interfaces;

/// <inheritdoc />
public class PerformanceMeasurer : IPerformanceMeasurer {
    private long _measure;
    private long _lastTimeInMilliseconds;
    private long _sampledMetricsCount;

    /// <inheritdoc />
    public long ValuePerMillisecond { get; private set; }

    /// <inheritdoc />
    public long ValuePerSecond => ValuePerMillisecond * 1000;

    /// <inheritdoc />
    public long AverageValuePerSecond { get; private set; }

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    public PerformanceMeasurer() => _lastTimeInMilliseconds = GetCurrentTime();

    private long _firstMeasureTimeInMilliseconds = 0;

    private const int WindowSizeInSeconds = 30;

    private static long GetCurrentTime() => System.Environment.TickCount64;

    /// <inheritdoc />
    public void UpdateValue(long newMeasure) {
        long newTimeInMilliseconds = GetCurrentTime();
        if (IsFirstMeasurement()) {
            _firstMeasureTimeInMilliseconds = newTimeInMilliseconds;
        } else if (IsLastMeasurementExpired(newTimeInMilliseconds)) {
            ResetMetrics(newTimeInMilliseconds);
        }

        long millisecondsDelta = newTimeInMilliseconds - _lastTimeInMilliseconds;
        if (millisecondsDelta == 0) {
            return;
        }
        _lastTimeInMilliseconds = newTimeInMilliseconds;
        long valueDelta = newMeasure - _measure;
        _measure = newMeasure;
        ValuePerMillisecond = valueDelta / millisecondsDelta;
        AverageValuePerSecond = ApproxRollingAverage(AverageValuePerSecond, ValuePerSecond, _sampledMetricsCount++);
    }

    private bool IsFirstMeasurement() {
        return _firstMeasureTimeInMilliseconds == 0;
    }

    private void ResetMetrics(long newTimeInMilliseconds) {
        _firstMeasureTimeInMilliseconds = newTimeInMilliseconds;
        _sampledMetricsCount = 0;
        AverageValuePerSecond = 0;
    }

    private bool IsLastMeasurementExpired(long newTimeInMilliseconds) =>
        newTimeInMilliseconds - _firstMeasureTimeInMilliseconds > WindowSizeInSeconds * 1000;

    private static long ApproxRollingAverage(long measureAverage, long valuePerSecond, long sampledMetricsCount) {
        measureAverage -= measureAverage / Math.Max(sampledMetricsCount, 1);
        measureAverage += valuePerSecond / Math.Max(sampledMetricsCount, 1);
        return measureAverage;
    }
}