namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

/// <summary>
///     Factory for creating cycle limiters based on <see cref="Configuration.Cycles" />.
/// </summary>
public static class CycleLimiterFactory {
    /// <summary>
    ///     Creates an appropriate cycle limiter based on the provided configuration.
    /// </summary>
    /// <param name="configuration">The emulator configuration.</param>
    /// <returns>A cycle limiter instance.</returns>
    public static ICyclesLimiter Create(Configuration configuration) {
        // Priority order: explicit Cycles setting, then InstructionsPerSecond, then default
        if (configuration.Cycles != null) {
            return new CpuCycleLimiter(configuration.Cycles.Value);
        }
        
        if (configuration.InstructionsPerSecond != null) {
            // Convert instructions per second to cycles per millisecond with proper rounding
            int cyclesPerMs = (int)Math.Round(configuration.InstructionsPerSecond.Value / 1000.0);
            return new CpuCycleLimiter(cyclesPerMs);
        }
        
        return new CpuCycleLimiter(ICyclesLimiter.RealModeCpuCyclesPerMs);
    }
}