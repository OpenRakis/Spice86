namespace Spice86.Shared.Diagnostics;

using Spice86.Shared.Interfaces;

/// <inheritdoc cref="IPerformanceMeasureReader" />
public class PerformanceMeasurer : IPerformanceMeasureReader, IPerformanceMeasureWriter {
    private long _measure;
    private long _lastTimeInMilliseconds;

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

    private static long GetCurrentTime() {
        return Environment.TickCount64;
    }

    /// <inheritdoc />
    public void UpdateValue(long newMeasure) {
        long newTimeInMilliseconds = GetCurrentTime();
        if (IsFirstMeasurement()) {
            _firstMeasureTimeInMilliseconds = newTimeInMilliseconds;
            _lastTimeInMilliseconds = newTimeInMilliseconds;
            _measure = newMeasure;
            return;
        }

        if (IsLastMeasurementExpired(newTimeInMilliseconds)) {
            ResetMetrics(newTimeInMilliseconds, newMeasure);
            return;
        }

        long millisecondsDelta = newTimeInMilliseconds - _lastTimeInMilliseconds;
        if (millisecondsDelta == 0) {
            return;
        }

        _lastTimeInMilliseconds = newTimeInMilliseconds;
        long valueDelta = newMeasure - _measure;
        _measure = newMeasure;
        ValuePerMillisecond = valueDelta / millisecondsDelta;
        long valuePerSecond = ValuePerSecond;
        AverageValuePerSecond = AverageValuePerSecond == 0
            ? valuePerSecond
            : SmoothingAverage(AverageValuePerSecond, valuePerSecond);
    }

    private bool IsFirstMeasurement() {
        return _firstMeasureTimeInMilliseconds == 0;
    }

    private void ResetMetrics(long newTimeInMilliseconds, long newMeasure) {
        _firstMeasureTimeInMilliseconds = newTimeInMilliseconds;
        _measure = newMeasure;
        _lastTimeInMilliseconds = newTimeInMilliseconds;
        ValuePerMillisecond = 0;
        AverageValuePerSecond = 0;
    }

    private bool IsLastMeasurementExpired(long newTimeInMilliseconds) =>
        newTimeInMilliseconds - _firstMeasureTimeInMilliseconds > WindowSizeInSeconds * 1000;

    private static long SmoothingAverage(long currentAverage, long latestValuePerSecond) {
        const double alpha = 0.2;
        return (long)((currentAverage * (1 - alpha)) + (latestValuePerSecond * alpha));
    }
}
