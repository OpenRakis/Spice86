namespace Spice86.Core.Emulator.CPU.CfgCpu.Linker;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Runtime.CompilerServices;

public class NodeLinker : InstructionReplacer {
    private readonly CfgNodeExecutionCompiler _executionCompiler;

    public NodeLinker(InstructionReplacerRegistry replacerRegistry, CfgNodeExecutionCompiler executionCompiler) : base(replacerRegistry) {
        _executionCompiler = executionCompiler;
    }

    /// <summary>
    /// Ensure current and next are linked together.
    /// Returns the resolved node to execute (may differ from <paramref name="next"/> when a SelectorNode is injected).
    /// </summary>
    /// <param name="linkToNextType"></param>
    /// <param name="current"></param>
    /// <param name="next"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ICfgNode Link(InstructionSuccessorType linkToNextType, ICfgNode current, ICfgNode next) {
        switch (current) {
            case IReturnInstruction retInstruction:
                // Special cases for ret.
                // We not only attach next but also the return target to the list of next for the corresponding call.
                // This involves recording data via the Call Flow Handler and linking it in a special way here.
                return LinkRetInstruction(linkToNextType, retInstruction, next);
            case CfgInstruction currentCfgInstruction:
                return LinkCfgInstructionWithType(linkToNextType, currentCfgInstruction, next);
            case SelectorNode selectorNode:
                LinkSelectorNode(selectorNode, next);
                return next;
            default:
                throw new UnhandledCfgDiscrepancyException(
                    $"Unhandled ICfgNode type in Link: {current.GetType().Name}");
        }
    }

    private ICfgNode LinkRetInstruction(InstructionSuccessorType linkToNextType, IReturnInstruction returnInstruction, ICfgNode next) {
        ICfgNode resolvedForRet = LinkCfgInstructionWithType(linkToNextType, returnInstruction, next);
        // Need to link the call instruction now that ret is known 
        CfgInstruction? callInstruction = returnInstruction.CurrentCorrespondingCallInstruction;
        returnInstruction.CurrentCorrespondingCallInstruction = null;
        if (callInstruction == null) {
            // No call associated with this ret. Nothing to do.
            return resolvedForRet;
        }
        InstructionSuccessorType type = ComputeSuccessorTypeForRet(callInstruction, next);
        // call->next is bookkeeping only, do not return the resolved value
        LinkCfgInstructionWithType(type, callInstruction, next);
        return resolvedForRet;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ICfgNode LinkCfgInstructionWithType(InstructionSuccessorType linkToNextType, ICfgInstruction current, ICfgNode next) {
        Dictionary<SegmentedAddress, ICfgNode> successors = current.SuccessorsPerAddress;
        if (!successors.TryGetValue(next.Address, out ICfgNode? shouldBeNext)) {
            // New link found
            AttachNewLink(linkToNextType, current, next);
            return next;
        }

        if (!ReferenceEquals(shouldBeNext, next)) {
            return ResolveSuccessorConflict(current, next, shouldBeNext);
        }
        return next;
    }

    private void AttachNewLink(InstructionSuccessorType linkToNextType, ICfgInstruction current, ICfgNode next) {
        AttachToNext(current, next);
        if (!current.SuccessorsPerType.TryGetValue(linkToNextType, out ISet<ICfgNode>? successorsForType)) {
            successorsForType = new HashSet<ICfgNode>();
            current.SuccessorsPerType[linkToNextType] = successorsForType;
        }
        successorsForType.Add(next);
    }

    private ICfgNode ResolveSuccessorConflict(ICfgInstruction current, ICfgNode next, ICfgNode shouldBeNext) {
        if (shouldBeNext is SelectorNode selectorNode) {
            LinkSelectorNode(selectorNode, next);
            return selectorNode;
        }
        return CreateSelectorNodeBetween((CfgInstruction)shouldBeNext, (CfgInstruction)next);
    }

    /// <summary>
    /// Creates a SelectorNode at the address of the two instructions and inserts it as an intermediate predecessor of both.
    /// </summary>
    public SelectorNode CreateSelectorNodeBetween(CfgInstruction instruction1, CfgInstruction instruction2) {
        SelectorNode selectorNode = new SelectorNode(instruction1.Address);
        //_executionCompiler.Compile(selectorNode);
        InsertIntermediatePredecessor(instruction1, selectorNode);
        InsertIntermediatePredecessor(instruction2, selectorNode);
        return selectorNode;
    }

    private InstructionSuccessorType ComputeSuccessorTypeForRet(CfgInstruction call, ICfgNode nextAfterRet) {
        if (call.NextInMemoryAddress != nextAfterRet.Address) {
            // Instruction executed after ret is not the next instruction in memory from the call.
            return InstructionSuccessorType.CallToMisalignedReturn;
        }
        return InstructionSuccessorType.CallToReturn;
    }

    private void LinkSelectorNode(SelectorNode current, ICfgNode next) {
        if (next is CfgInstruction nextCfgInstruction) {
            Dictionary<Signature, CfgInstruction> successors = current.SuccessorsPerSignature;
            if (!successors.TryGetValue(nextCfgInstruction.Signature, out CfgInstruction? shouldBeNextOrNull)) {
                // New link discovered, create it
                AttachToNext(current, next);
                return;
            }
            if (!ReferenceEquals(shouldBeNextOrNull, next)) {
                throw new UnhandledCfgDiscrepancyException("Next instruction's signature is present in the selector node successors, but the corresponding successor is not next instruction which should never happen.");
            }
        } else {
            throw new UnhandledCfgDiscrepancyException("Trying to attach a non ASM instruction to a selector node which is not allowed. This should never happen.");
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
        // Unique successor is only valid if node has 1 successor
        current.UniqueSuccessor = current.MaxSuccessorsCount == 1 ? next : null;
        if (current.Successors.Count == current.MaxSuccessorsCount) {
            // We reached the max number of successors for this node
            // This means that there is no need to try to link it to other nodes, it is impossible there will be new links.
            current.CanHaveMoreSuccessors = false;
        }
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
            // Replace current with new in the successor's predecessors (:
            LinkToNext(newNode, successor);
            successor.Predecessors.Remove(oldNode);
            // No cache update of newNode. Should be done at the end of the loop but caller already does it.
        }
        oldNode.Successors.Clear();
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
        oldNode.Predecessors.Clear();
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