namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.CLI;

/// <summary>
/// Factory for creating cycle limiters based on <see cref="Configuration.Cycles"/>.
/// </summary>
public static class CycleLimiterFactory {
    /// <summary>
    /// Creates an appropriate cycle limiter based on the provided configuration.
    /// </summary>
    /// <param name="configuration">The emulator configuration.</param>
    /// <returns>A cycle limiter instance.</returns>
    public static CycleLimiterBase Create(Configuration configuration) {
        return configuration.Cycles switch {
            null => new NullCycleLimiter(),
            _ => new CpuCycleThrottler(configuration.Cycles.Value)
        };
    }
}