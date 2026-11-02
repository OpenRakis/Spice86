namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// The stable order of <see cref="CfgBlock"/> used by the dominator algorithms: entry address, then block id.
/// </summary>
internal sealed class CfgBlockOrder : IComparer<CfgBlock> {
    /// <summary>
    /// Prevents instantiation.
    /// </summary>
    private CfgBlockOrder() {
    }

    /// <summary>
    /// The shared instance.
    /// </summary>
    public static CfgBlockOrder Instance { get; } = new CfgBlockOrder();

    /// <summary>
    /// Compares two blocks by entry address, then by id.
    /// </summary>
    /// <param name="x">The first block.</param>
    /// <param name="y">The second block.</param>
    /// <returns>A signed comparison result.</returns>
    public int Compare(CfgBlock? x, CfgBlock? y) {
        if (ReferenceEquals(x, y)) {
            return 0;
        }
        if (x is null) {
            return -1;
        }
        if (y is null) {
            return 1;
        }
        int result = x.Entry.Address.CompareTo(y.Entry.Address);
        return result != 0 ? result : x.Id.CompareTo(y.Id);
    }
}
