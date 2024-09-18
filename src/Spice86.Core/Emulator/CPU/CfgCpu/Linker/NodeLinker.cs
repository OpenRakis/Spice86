namespace Spice86.Core.Emulator.CPU.CfgCpu.Linker;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Runtime.CompilerServices;

public class NodeLinker : InstructionReplacer {
    private readonly NodeToString _nodeToString = new();

    public NodeLinker(InstructionReplacerRegistry replacerRegistry) : base(replacerRegistry) {
    }

    /// <summary>
    /// Ensure current and next are linked together.
    /// </summary>
    /// <param name="current"></param>
    /// <param name="next"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Link(ICfgNode current, ICfgNode next) {
        switch (current) {
            case IReturnInstruction retInstruction:
                // Special cases for ret.
                // We not only attach next but also the return target to the list of next for the corresponding call.
                // This involves recording data via the Call Flow Handler and linking it in a special way here.
                LinkRetInstruction(retInstruction, next);
                break;
            case CfgInstruction currentCfgInstruction:
                LinkCfgInstructionWithType(InstructionSuccessorType.Normal, currentCfgInstruction, next);
                break;
            case DiscriminatedNode discriminatedNode:
                LinkDiscriminatedNode(discriminatedNode, next);
                break;
        }
    }

    private void LinkRetInstruction(IReturnInstruction returnInstruction, ICfgNode next) {
        LinkCfgInstructionWithType(InstructionSuccessorType.Normal, returnInstruction, next);
        // Need to link the call instruction now that ret is known 
        CfgInstruction? callInstruction = returnInstruction.CurrentCorrespondingCallInstruction;
        returnInstruction.CurrentCorrespondingCallInstruction = null;
        if (callInstruction == null) {
            // No call associated with this ret. Nothing to do.
            return;
        }
        InstructionSuccessorType type = ComputeSuccessorTypeForRet(callInstruction, next);
        LinkCfgInstructionWithType(type, callInstruction, next);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LinkCfgInstructionWithType(InstructionSuccessorType type, ICfgInstruction current, ICfgNode next) {
        Dictionary<SegmentedAddress, ICfgNode> successors = current.SuccessorsPerAddress;
        if (!successors.TryGetValue(next.Address, out ICfgNode? shouldBeNext)) {
            // New link found
            AttachNewLink(type, current, next);
            return;
        }

        if (!ReferenceEquals(shouldBeNext, next)) {
            throw ThrowSuccessorConflictError(current, next, shouldBeNext);
        }
    }

    private void AttachNewLink(InstructionSuccessorType type, ICfgInstruction current, ICfgNode next) {
        AttachToNext(current, next);
        if (!current.SuccessorsPerType.TryGetValue(type, out ISet<ICfgNode>? successorsForType)) {
            successorsForType = new HashSet<ICfgNode>();
            current.SuccessorsPerType[type] = successorsForType;
        }
        successorsForType.Add(next);
    }

    private UnhandledCfgDiscrepancyException ThrowSuccessorConflictError(ICfgInstruction current, ICfgNode next, ICfgNode shouldBeNext) {
        string nextToString = _nodeToString.ToString(next);
        string shouldBeNextToString = _nodeToString.ToString(shouldBeNext);
        string currentToString = _nodeToString.ToString(current);
        string currentSuccessors = _nodeToString.SuccessorsToString(current);
        return new UnhandledCfgDiscrepancyException(
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

    private InstructionSuccessorType ComputeSuccessorTypeForRet(CfgInstruction call, ICfgNode nextAfterRet) {
        if (call.NextInMemoryAddress != nextAfterRet.Address) {
            // Instruction executed after ret is not the next instruction in memory from the call.
            return InstructionSuccessorType.CallToMisalignedReturn;
        }
        return InstructionSuccessorType.CallToReturn;
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
    
    private void ReplaceSuccessorsPerType(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        // Merge the SuccessorsPerType with the new instruction
        foreach (KeyValuePair<InstructionSuccessorType, ISet<ICfgNode>> oldEntry in oldInstruction.SuccessorsPerType) {
            if (!newInstruction.SuccessorsPerType.TryGetValue(oldEntry.Key, out ISet<ICfgNode>? successorsForType)) {
                // No key => replace with new
                successorsForType = oldEntry.Value;
            } else {
                // Key => merge the sets
                successorsForType = new HashSet<ICfgNode>(successorsForType.Concat(oldEntry.Value));
            }
            newInstruction.SuccessorsPerType[oldEntry.Key] = successorsForType;
        }
    }

    private void AttachToNext(ICfgNode current, ICfgNode next) {
        LinkToNext(current, next);
        current.UpdateSuccessorCache();
    }

    private void LinkToNext(ICfgNode current, ICfgNode next) {
        current.Successors.Add(next);
        next.Predecessors.Add(current);
    }

    public override void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        // Switch predecessors and successors of old to new
        SwitchPredecessorsToNew(oldInstruction, newInstruction);
        SwitchSuccessorsToNew(oldInstruction, newInstruction);
        // Keep data from SuccessorsPerType map of old
        ReplaceSuccessorsPerType(oldInstruction, newInstruction);
        // Update caches
        newInstruction.UpdateSuccessorCache();
        oldInstruction.UpdateSuccessorCache();
    }

    /// <summary>
    /// Inserts newPredecessor as an intermediate node between current and its own predecessors
    /// </summary>
    /// <param name="current"></param>
    /// <param name="newPredecessor"></param>
    public void InsertIntermediatePredecessor(ICfgNode current, ICfgNode newPredecessor) {
        SwitchPredecessorsToNew(current, newPredecessor);
        // Make new the only predecessor of current
        current.Predecessors.Clear();
        LinkToNext(newPredecessor, current);
        newPredecessor.UpdateSuccessorCache();
    }

    private void SwitchSuccessorsToNew(ICfgNode oldNode, ICfgNode newNode) {
        foreach (ICfgNode successor in oldNode.Successors) {
            LinkToNext(newNode, successor);
            successor.Predecessors.Remove(oldNode);
            oldNode.Successors.Remove(successor);
        }
    }

    private void SwitchPredecessorsToNew(ICfgNode oldNode, ICfgNode newNode) {
        foreach (ICfgNode predecessor in oldNode.Predecessors) {
            // Replace current with new in the predecessors successors (:
            LinkToNext(predecessor, newNode);
            predecessor.Successors.Remove(oldNode);
            predecessor.UpdateSuccessorCache();
            if (predecessor is CfgInstruction predecessorInstruction) {
                ReplaceSuccessorOfCallInstruction(predecessorInstruction, oldNode, newNode);
            }
        }
    }

    private void ReplaceSuccessorOfCallInstruction(CfgInstruction instruction, ICfgNode currentSuccesor, ICfgNode newSuccesor) {
        foreach(KeyValuePair<InstructionSuccessorType, ISet<ICfgNode>> entry in instruction.SuccessorsPerType) {
            ISet<ICfgNode> successors = entry.Value;
            if (successors.Contains(currentSuccesor)) {
                successors.Remove(currentSuccesor);
                successors.Add(newSuccesor);
            }
        }
    }
}