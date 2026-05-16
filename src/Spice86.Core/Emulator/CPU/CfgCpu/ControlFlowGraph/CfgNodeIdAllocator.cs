namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Threading;

/// <summary>
/// Thread-safe, instance-scoped ID counter for CFG nodes.
/// One instance per emulator run ensures IDs are deterministic and independent across runs.
/// </summary>
public sealed class CfgNodeIdAllocator {
    private int _nextId = -1;

    /// <summary>
    /// Allocates and returns the next available node ID.
    /// </summary>
    public int AllocateId() => Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Sets the next ID to be allocated. Used when importing pre-existing blocks
    /// that carry known IDs, to resume allocation beyond the highest imported ID.
    /// </summary>
    public int NextId {
        set => Volatile.Write(ref _nextId, value - 1);
    }
}
