namespace Spice86.Shared.Diagnostics;

/// <summary>
///     Configures sampling behavior for <see cref="PerformanceMeasurer" /> instances.
/// </summary>
public sealed record PerformanceMeasureOptions {
    /// <summary>
    ///     Gets the default set of options (no throttling).
    /// </summary>
    public static PerformanceMeasureOptions Default { get; } = new();

    /// <summary>
    ///     Gets or inits how many <see cref="PerformanceMeasurer.UpdateValue(long)" /> calls should occur before
    ///     expensive calculations run. A value of 1 disables interval-based throttling.
    /// </summary>
    public int CheckInterval { get; init; } = 1;

    /// <summary>
    ///     Gets or inits the minimum delta between measurements required to trigger a recalculation.
    /// </summary>
    public long MinValueDelta { get; init; }

    /// <summary>
    ///     Gets or inits the maximum amount of time (in milliseconds) allowed between recalculations.
    ///     A value of 0 disables the time-based guard.
    /// </summary>
    public int MaxIntervalMilliseconds { get; init; }
}