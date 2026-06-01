namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Single definition of the derived-successor invariant: how <see cref="ICfgNode.UniqueSuccessor"/> and
/// <see cref="ICfgNode.CanHaveMoreSuccessors"/> follow from <see cref="ICfgNode.MaxSuccessorsCount"/> and
/// the node's current successor set.
///
/// <para>
/// Both the live <see cref="Linker.NodeLinker"/> (one edge at a time, during execution) and the CFG graph
/// reloader (whole successor set at once, during reload) must keep this tuple consistent. Defining it once
/// here prevents the two paths from drifting if the rule ever changes.
/// </para>
/// </summary>
public static class SuccessorInvariant {
    /// <summary>
    /// Recomputes <see cref="ICfgNode.UniqueSuccessor"/> and <see cref="ICfgNode.CanHaveMoreSuccessors"/>
    /// from <paramref name="node"/>'s current successors and successor cap. Does not touch the successor
    /// cache or any edge sets; callers own those. Allocation-free: it is on the live link hot path.
    /// </summary>
    public static void Refresh(ICfgNode node) {
        int? maxSuccessors = node.MaxSuccessorsCount;
        node.UniqueSuccessor = maxSuccessors == 1 ? FirstOrNull(node.Successors) : null;
        node.CanHaveMoreSuccessors = maxSuccessors is null || node.Successors.Count < maxSuccessors;
    }

    private static ICfgNode? FirstOrNull(HashSet<ICfgNode> successors) {
        // foreach over the concrete HashSet uses its struct enumerator and allocates nothing.
        foreach (ICfgNode successor in successors) {
            return successor;
        }
        return null;
    }
}
