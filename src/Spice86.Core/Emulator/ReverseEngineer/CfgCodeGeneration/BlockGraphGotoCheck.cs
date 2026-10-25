namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Linq;

/// <summary>
/// Checks that the lowering only jumps along edges of the method's block graph: a <c>goto</c> of a node that is
/// not the last of its block targets the next node of the block, and a <c>goto</c> of the last node targets the entry
/// of a successor block. A <c>goto</c> outside the graph would be invisible to everything that uses the graph (layout,
/// event checks, flag liveness, loops), so it is a generator bug.
/// </summary>
internal static class BlockGraphGotoCheck {
    /// <summary>Throws when a lowered node jumps outside <paramref name="graph"/>.</summary>
    /// <param name="plans">The emission plans of the method, in emission order.</param>
    /// <param name="boundaries">The lowered node of each plan, same order, before any <c>goto</c> is removed.</param>
    /// <param name="graph">The block graph of the method.</param>
    /// <param name="methodName">The method name, for the message.</param>
    /// <exception cref="InvalidOperationException">A <c>goto</c> is not an edge of <paramref name="graph"/>.</exception>
    public static void Check(IReadOnlyList<NodeEmissionPlan> plans, IReadOnlyList<InstructionBoundaryNode> boundaries,
        PartitionBlockGraph graph, string methodName) {
        for (int index = 0; index < plans.Count; index++) {
            NodeEmissionPlan plan = plans[index];
            HashSet<ICfgNode> allowed = AllowedTargets(plans, index, graph);
            foreach (ICfgNode target in GotoTargetCollector.Collect(boundaries[index]).Labels) {
                if (!allowed.Contains(target)) {
                    throw new InvalidOperationException(
                        $"Generated method {methodName}: the goto from {plan.Node.Address} to {target.Address} is not an edge of the block graph.");
                }
            }
        }
    }

    private static HashSet<ICfgNode> AllowedTargets(IReadOnlyList<NodeEmissionPlan> plans, int index, PartitionBlockGraph graph) {
        NodeEmissionPlan plan = plans[index];
        if (index + 1 < plans.Count && ReferenceEquals(plans[index + 1].Block, plan.Block)) {
            return [plans[index + 1].Node];
        }
        return graph.GetSuccessors(plan.Block).Select(successor => successor.Entry).ToHashSet();
    }
}
