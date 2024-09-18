namespace Spice86.Core.Emulator.CPU.CfgCpu.Linker;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Shared.Emulator.Memory;

public class NodeLinker : InstructionReplacer {
    private readonly NodeToString _nodeToString = new();

    public NodeLinker(InstructionReplacerRegistry replacerRegistry) : base(replacerRegistry) {
    }

    /// <summary>
    /// Ensure current and next are linked together.
    /// </summary>
    /// <param name="current"></param>
    /// <param name="next"></param>
    public void Link(ICfgNode current, ICfgNode next) {
        if (current is CfgInstruction currentCfgInstruction) {
            LinkCfgInstruction(currentCfgInstruction, next);
        } else if (current is DiscriminatedNode discriminatedNode) {
            LinkDiscriminatedNode(discriminatedNode, next);
        }
    }

    private void LinkCfgInstruction(CfgInstruction current, ICfgNode next) {
        Dictionary<SegmentedAddress, ICfgNode> successors = current.SuccessorsPerAddress;
        if (!successors.TryGetValue(next.Address, out ICfgNode? shouldBeNext)) {
            // New link found
            AttachToNext(current, next);
            return;
        }

        if (!ReferenceEquals(shouldBeNext, next)) {
            string nextToString = _nodeToString.ToString(next);
            string shouldBeNextToString = _nodeToString.ToString(shouldBeNext);
            string currentToString = _nodeToString.ToString(current);
            string currentSuccessors = _nodeToString.SuccessorsToString(current);
            throw new UnhandledCfgDiscrepancyException(
                $"""
                 Current node has already a successor at next node address but it is not the next node. This should never happen. 
                 Details:
                 Next {nextToString}
                 Found instead {shouldBeNextToString}
                 Current node {currentToString}
                 Current successors
                 {currentSuccessors}
                 """);
        }
    }

    private void LinkDiscriminatedNode(DiscriminatedNode current, ICfgNode next) {
        if (next is CfgInstruction nextCfgInstruction) {
            Dictionary<Discriminator, CfgInstruction> successors = current.SuccessorsPerDiscriminator;
            if (!successors.TryGetValue(nextCfgInstruction.Discriminator, out CfgInstruction? shouldBeNextOrNull)) {
                // New link discovered, create it
                AttachToNext(current, next);
                return;
            }
            if (!ReferenceEquals(shouldBeNextOrNull, next)) {
                throw new UnhandledCfgDiscrepancyException("Next instruction's discriminator is present in the discriminated node successors, but the corresponding successor is not next instruction which should never happen.");
            }
        } else {
            throw new UnhandledCfgDiscrepancyException("Trying to attach a non ASM instruction to a discriminated node which is not allowed. This should never happen.");
        }
    }

    public void AttachToNext(ICfgNode current, ICfgNode next) {
        LinkToNext(current, next);
        current.UpdateSuccessorCache();
    }

    private void LinkToNext(ICfgNode current, ICfgNode next) {
        current.Successors.Add(next);
        next.Predecessors.Add(current);
    }

    public override void ReplaceInstruction(CfgInstruction old, CfgInstruction instruction) {
        // Unlinks old from the graph and links instruction instead, effectively replacing it
        InsertIntermediatePredecessor(old, instruction);
        foreach (ICfgNode successor in old.Successors) {
            LinkToNext(instruction, successor);
            successor.Predecessors.Remove(old);
            old.Successors.Remove(successor);
        }

        instruction.UpdateSuccessorCache();
        old.UpdateSuccessorCache();
    }

    public void InsertIntermediatePredecessor(ICfgNode current, ICfgNode newPredecessor) {
        foreach (ICfgNode predecessor in current.Predecessors) {
            // Replace current with new in the predecessors successors (:
            LinkToNext(predecessor, newPredecessor);
            predecessor.Successors.Remove(current);
            predecessor.UpdateSuccessorCache();
        }
        // Make new the only predecessor of current
        current.Predecessors.Clear();
        LinkToNext(newPredecessor, current);
        newPredecessor.UpdateSuccessorCache();
    }
}