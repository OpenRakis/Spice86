namespace Spice86.Shared.Diagnostics;

using Spice86.Shared.Interfaces;

/// <inheritdoc cref="IPerformanceMeasureReader" />
public class PerformanceMeasurer : IPerformanceMeasureReader, IPerformanceMeasureWriter {
    private const int WindowSizeInSeconds = 30;
    private readonly int _checkInterval;
    private readonly bool _hasSamplingConstraints;
    private readonly int _maxIntervalMilliseconds;
    private readonly long _minValueDelta;
    private long _firstMeasureTimeInMilliseconds;
    private long _lastTimeInMilliseconds;

    private long _measure;
    private int _samplingCountdown;

    /// <summary>
    ///     Initializes a new instance.
    /// </summary>
    public PerformanceMeasurer()
        : this(PerformanceMeasureOptions.Default) {
    }

    /// <summary>
    ///     Initializes a new instance with custom sampling options.
    /// </summary>
    /// <param name="options">Sampling options that control how frequently expensive calculations run.</param>
    public PerformanceMeasurer(PerformanceMeasureOptions? options) {
        PerformanceMeasureOptions sanitized = options ?? PerformanceMeasureOptions.Default;
        _checkInterval = Math.Max(1, sanitized.CheckInterval);
        _minValueDelta = Math.Max(0, sanitized.MinValueDelta);
        _maxIntervalMilliseconds = Math.Max(0, sanitized.MaxIntervalMilliseconds);
        _hasSamplingConstraints = _checkInterval > 1 || _minValueDelta > 0 || _maxIntervalMilliseconds > 0;
        _samplingCountdown = _hasSamplingConstraints ? 1 : 0;
    }

    /// <inheritdoc />
    public long ValuePerMillisecond { get; private set; }

    /// <inheritdoc />
    public long ValuePerSecond => ValuePerMillisecond * 1000;

    /// <inheritdoc />
    public long AverageValuePerSecond { get; private set; }

    /// <inheritdoc />
    public void UpdateValue(long newMeasure) {
        long newTimeInMilliseconds = GetCurrentTime();
        if (IsFirstMeasurement()) {
            InitializeFirstMeasurement(newMeasure, newTimeInMilliseconds);
            return;
        }

        if (IsLastMeasurementExpired(newTimeInMilliseconds)) {
            ResetMetrics(newTimeInMilliseconds, newMeasure);
            return;
        }

        if (!ShouldProcessSample(newMeasure, newTimeInMilliseconds)) {
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

    private static long GetCurrentTime() {
        return Environment.TickCount64;
    }

    private bool ShouldProcessSample(long newMeasure, long newTimeInMilliseconds) {
        if (!_hasSamplingConstraints) {
            return true;
        }

        if (--_samplingCountdown > 0) {
            return false;
        }

        ResetSamplingCountdown();

        long valueDelta = newMeasure - _measure;
        long timeDelta = newTimeInMilliseconds - _lastTimeInMilliseconds;
        return valueDelta >= _minValueDelta || (_maxIntervalMilliseconds != 0 && timeDelta >= _maxIntervalMilliseconds);
    }

    private bool IsFirstMeasurement() {
        return _firstMeasureTimeInMilliseconds == 0;
    }

    private void InitializeFirstMeasurement(long newMeasure, long newTimeInMilliseconds) {
        _firstMeasureTimeInMilliseconds = newTimeInMilliseconds;
        _measure = newMeasure;
        _lastTimeInMilliseconds = newTimeInMilliseconds;
        ValuePerMillisecond = 0;
        AverageValuePerSecond = 0;
        ResetSamplingCountdown();
    }

    private void ResetMetrics(long newTimeInMilliseconds, long newMeasure) {
        _firstMeasureTimeInMilliseconds = newTimeInMilliseconds;
        _measure = newMeasure;
        _lastTimeInMilliseconds = newTimeInMilliseconds;
        ValuePerMillisecond = 0;
        AverageValuePerSecond = 0;
        ResetSamplingCountdown();
    }

    private void ResetSamplingCountdown() {
        if (_hasSamplingConstraints) {
            _samplingCountdown = _checkInterval;
        }
    }

    private bool IsLastMeasurementExpired(long newTimeInMilliseconds) {
        return newTimeInMilliseconds - _firstMeasureTimeInMilliseconds > WindowSizeInSeconds * 1000;
    }

    private static long SmoothingAverage(long currentAverage, long latestValuePerSecond) {
        const double alpha = 0.2; // Exponential smoothing factor (20% weight for latest value).
        return (long)((currentAverage * (1 - alpha)) + (latestValuePerSecond * alpha));
    }
}