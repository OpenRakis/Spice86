namespace Spice86.Core.CLI;

/// <summary>
/// Controls how the CfgCpu JIT compiler handles instruction execution delegates.
/// </summary>
public enum JitMode {
    /// <summary>
    /// Assigns an interpreted delegate immediately and swaps in an optimized compiled delegate
    /// in the background once compilation finishes. This is the default mode, balancing
    /// low first-execution latency with long-term throughput.
    /// </summary>
    InterpretedThenCompiled,

    /// <summary>
    /// Only uses interpreted delegates. Background compilation threads are not started.
    /// Useful for debugging or on memory-constrained systems.
    /// </summary>
    InterpretedOnly,

    /// <summary>
    /// Compiles each instruction synchronously on first encounter and assigns the compiled
    /// delegate directly, skipping the interpreted phase entirely. Increases startup latency
    /// but avoids the overhead of running two delegate forms.
    /// </summary>
    CompiledOnly
}
