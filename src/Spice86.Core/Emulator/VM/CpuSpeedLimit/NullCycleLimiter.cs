namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// A no-operation cycle limiter that performs no throttling.
/// </summary>
/// <remarks>
/// This is used, unless --Cycles is specified on the command line.
/// </remarks>
public class NullCycleLimiter : CycleLimiterBase {
    public override void DecreaseCycles() {
        // Do nothing - run at full speed

    }

    public override void IncreaseCycles() {
        // Do nothing - we already run at full speed
    }

    /// <inheritdoc/>
    internal override void RegulateCycles(State cpuState) {
        // Do nothing - run at full speed
    }
}
