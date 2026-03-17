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
        if (configuration.Cycles is null) {
            return new NullCyclesLimiter();
        }

        return new CpuCycleLimiter(state, configuration.Cycles.Value);
    }
}