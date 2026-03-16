namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;

/// <summary>
///     Factory for creating cycle limiters based on <see cref="Configuration.Cycles" />.
/// </summary>
public static class CycleLimiterFactory {
    /// <summary>
    ///     Creates an appropriate cycle limiter based on the provided configuration.
    /// </summary>
    /// <param name="state">The CPU state, used for Cycles and IsRunning properties.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <returns>A cycle limiter instance.</returns>
    public static ICyclesLimiter Create(State state, Configuration configuration) {
        if (configuration.Cycles is null && configuration.InstructionsPerSecond is null) {
            return new NullCyclesLimiter();
        }
        // Priority order: explicit Cycles setting, then InstructionsPerSecond, then default
        if (configuration.Cycles is not null) {
            return new CpuCycleLimiter(state, configuration.Cycles.Value);
        }
        
        if (configuration.InstructionsPerSecond != null) {
            // Convert instructions per second to cycles per millisecond with proper rounding
            int cyclesPerMs = (int)Math.Round(configuration.InstructionsPerSecond.Value / 1000.0);
            return new CpuCycleLimiter(state, cyclesPerMs);
        }
        
        return new CpuCycleLimiter(state, ICyclesLimiter.RealModeCpuCyclesPerMs);
    }
}