namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using System.Linq;

using Xunit.Sdk;

/// <summary>
/// Test-only invariant check over a node's successor bookkeeping. Every secondary successor collection
/// must agree with the authoritative <see cref="ICfgNode.Successors"/> set, predecessor edges must be
/// symmetric, and the derived scalars must match <see cref="SuccessorInvariant"/>. Applied after each
/// edge-mutation path, it turns accidental drift (one metadata set mutated without the others) into an
/// explicit test failure.
/// </summary>
internal static class SuccessorConsistency {
    public static void AssertConsistent(ICfgNode node) {
        if (node is CfgBlock) {
            // A block delegates its successor semantics to its terminator; nothing to check here.
            return;
        }
        AssertBidirectional(node);
        AssertDerivedScalars(node);
        switch (node) {
            case CfgInstruction instruction:
                AssertInstruction(instruction);
                break;
            case SelectorNode selector:
                AssertSelector(selector);
                break;
        }
    }

    private static void AssertBidirectional(ICfgNode node) {
        foreach (ICfgNode successor in node.Successors.Where(s => !s.Predecessors.Contains(node))) {
            throw new XunitException(
                $"Successor {successor.Id} of node {node.Id} does not list it as a predecessor.");
        }
        foreach (ICfgNode predecessor in node.Predecessors.Where(p => !p.Successors.Contains(node))) {
            throw new XunitException(
                $"Predecessor {predecessor.Id} of node {node.Id} does not list it as a successor.");
        }
    }

    private static void AssertDerivedScalars(ICfgNode node) {
        int? maxSuccessors = node.MaxSuccessorsCount;
        bool expectedCanHaveMore = maxSuccessors is null || node.Successors.Count < maxSuccessors;
        if (node.CanHaveMoreSuccessors != expectedCanHaveMore) {
            throw new XunitException(
                $"Node {node.Id} CanHaveMoreSuccessors={node.CanHaveMoreSuccessors} disagrees with successor count " +
                $"{node.Successors.Count} and cap {maxSuccessors?.ToString() ?? "null"}.");
        }
        if (maxSuccessors == 1) {
            if (node.Successors.Count == 0) {
                if (node.UniqueSuccessor is not null) {
                    throw new XunitException(
                        $"Node {node.Id} has UniqueSuccessor set but no successors.");
                }
            } else if (node.UniqueSuccessor is null || !node.Successors.Contains(node.UniqueSuccessor)) {
                throw new XunitException(
                    $"Node {node.Id} UniqueSuccessor is not among its successors.");
            }
        } else if (node.UniqueSuccessor is not null) {
            throw new XunitException(
                $"Node {node.Id} has UniqueSuccessor set but its successor cap is not 1.");
        }
    }

    private static void AssertInstruction(CfgInstruction instruction) {
        if (instruction.SuccessorsPerAddress.Count != instruction.Successors.Count) {
            throw new XunitException(
                $"Instruction {instruction.Id} SuccessorsPerAddress has {instruction.SuccessorsPerAddress.Count} " +
                $"entries but {instruction.Successors.Count} successors.");
        }
        foreach (ICfgNode successor in instruction.Successors.Where(successor =>
            !instruction.SuccessorsPerAddress.TryGetValue(successor.Address, out ICfgNode? byAddress)
            || !byAddress.Equals(successor))) {
            throw new XunitException(
                $"Instruction {instruction.Id} SuccessorsPerAddress does not map {successor.Address} " +
                $"to successor {successor.Id}.");
        }
        Dictionary<ICfgNode, InstructionSuccessorType> typeBySuccessor = new();
        foreach ((InstructionSuccessorType type, ISet<ICfgNode> bucket) in instruction.SuccessorsPerType) {
            foreach (ICfgNode successor in bucket.Where(s => !instruction.Successors.Contains(s))) {
                throw new XunitException(
                    $"Instruction {instruction.Id} lists {successor.Id} under type {type} but it is not a successor.");
            }
            foreach (ICfgNode successor in bucket) {
                if (typeBySuccessor.TryGetValue(successor, out InstructionSuccessorType existingType)) {
                    throw new XunitException(
                        $"Instruction {instruction.Id} lists successor {successor.Id} under two types " +
                        $"({existingType} and {type}).");
                }
                typeBySuccessor[successor] = type;
            }
        }
        foreach (ICfgNode successor in instruction.SpeculativelyWiredSuccessors.Where(s => !instruction.Successors.Contains(s))) {
            throw new XunitException(
                $"Instruction {instruction.Id} marks {successor.Id} as speculatively wired but it is not a successor.");
        }
    }

    private static void AssertSelector(SelectorNode selector) {
        foreach (CfgInstruction successor in selector.SuccessorsPerSignature.Values.Where(s => !selector.Successors.Contains(s))) {
            throw new XunitException(
                $"Selector {selector.Id} lists successor {successor.Id} by signature but it is not in its successor set.");
        }
    }
}
