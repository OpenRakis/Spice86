namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Removes trampoline blocks: an empty block whose terminator is a plain jump forwards every edge that
/// targets it to the jump's own target, then disappears. An entry block, a block jumping to itself and a
/// block more than one edge targets are never removed: the last is a join, not a trampoline, and removing
/// it would split a two-arm conditional into diverging arms. Chains of trampolines collapse to the final
/// non-trampoline target in one pass.
/// </summary>
internal static class IrJumpThreading {
    /// <summary>Threads every trampoline of <paramref name="method"/> out of its edges and removes it.</summary>
    public static void Apply(IrMethod method) {
        HashSet<IrBlock> entryBlocks = method.Entries.Select(entry => entry.Block).ToHashSet();
        Dictionary<IrBlock, int> predecessorCounts = [];
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is null) {
                continue;
            }
            foreach (IrEdge edge in block.Terminator.Edges) {
                predecessorCounts[edge.Target] = predecessorCounts.GetValueOrDefault(edge.Target) + 1;
            }
        }
        Dictionary<IrBlock, IrBlock> jumps = [];
        foreach (IrBlock block in method.Blocks) {
            if (block.Statements.Count != 0 || block.Terminator is not IrJump jump
                || predecessorCounts.GetValueOrDefault(block) > 1
                || entryBlocks.Contains(block) || ReferenceEquals(jump.Edge.Target, block)) {
                continue;
            }
            jumps[block] = jump.Edge.Target;
        }
        if (jumps.Count == 0) {
            return;
        }
        foreach (IrBlock block in method.Blocks.Where(block => !jumps.ContainsKey(block))) {
            block.Terminator = Retarget(block.Terminator, jumps);
        }
        foreach (List<IrBlock> targets in method.StructuralSuccessors.Values) {
            for (int index = 0; index < targets.Count; index++) {
                targets[index] = Resolve(targets[index], jumps);
            }
        }
        method.Blocks.RemoveAll(jumps.ContainsKey);
    }

    private static IrTerminator Retarget(IrTerminator terminator, Dictionary<IrBlock, IrBlock> jumps) =>
        terminator switch {
            IrJump jump => new IrJump(jump.Origin, Retarget(terminator, jump.Edge, jumps)),
            IrBranch branch => new IrBranch(branch.Origin, branch.Condition,
                Retarget(terminator, branch.WhenTrue, jumps),
                Retarget(terminator, branch.WhenFalse, jumps)),
            IrSwitch switchNode => new IrSwitch(switchNode.Origin, switchNode.Value,
                switchNode.Cases.Select(switchCase =>
                    new IrSwitchCase(switchCase.Label, Retarget(terminator, switchCase.Target, jumps)))
                    .ToList(),
                Retarget(terminator, switchNode.Default, jumps)),
            _ => terminator
        };

    private static IrEdge Retarget(IrTerminator source, IrEdge edge, Dictionary<IrBlock, IrBlock> jumps) {
        IrBlock target = Resolve(edge.Target, jumps);
        if (ReferenceEquals(target, edge.Target)) {
            return edge;
        }
        bool inside = source.Origin.Equals(target.EntryOrigin) && !target.StartsInstruction;
        return new IrEdge(target, inside);
    }

    private static IrBlock Resolve(IrBlock target, Dictionary<IrBlock, IrBlock> jumps) {
        HashSet<IrBlock> seen = [target];
        while (jumps.TryGetValue(target, out IrBlock? next) && seen.Add(next)) {
            target = next;
        }
        return target;
    }
}
