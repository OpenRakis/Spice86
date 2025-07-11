namespace Spice86.Shared.Interfaces;

/// <summary>
/// Measures a performance metric.
/// </summary>
public interface IPerformanceMeasureReader {
    /// <summary>
    /// Gets the performance measurement value per millisecond.
    /// </summary>
    long ValuePerMillisecond { get; }

    /// <summary>
    /// Gets the performance measurement value per second.
    /// </summary>
    long ValuePerSecond { get; }

    /// <summary>
    /// Gets the performance measurement value per second (average).
    /// </summary>
    long AverageValuePerSecond { get; }
}

public interface IPerformanceMeasureWriter {
    /// <summary>
    /// Updates performance measurements with a new value.
    /// </summary>
    /// <param name="newMeasure">The new total amount for the metric being measured</param>
    void UpdateValue(long newMeasure);
}