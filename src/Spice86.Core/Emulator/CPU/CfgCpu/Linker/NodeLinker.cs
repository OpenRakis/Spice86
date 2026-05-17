namespace Spice86.Core.Emulator.CPU.CfgCpu.Linker;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Runtime.CompilerServices;

public class NodeLinker : InstructionReplacer {
    private readonly CfgNodeExecutionCompiler _executionCompiler;
    private readonly CfgNodeIdAllocator _idAllocator;

    public NodeLinker(InstructionReplacerRegistry replacerRegistry, CfgNodeExecutionCompiler executionCompiler, CfgNodeIdAllocator idAllocator) : base(replacerRegistry) {
        _executionCompiler = executionCompiler;
        _idAllocator = idAllocator;
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
            case CfgInstruction cfgInstr when cfgInstr.IsReturn:
                // Special cases for ret.
                // We not only attach next but also the return target to the list of next for the corresponding call.
                // This involves recording data via the Call Flow Handler and linking it in a special way here.
                return LinkRetInstruction(linkToNextType, cfgInstr, next);
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

    private ICfgNode LinkRetInstruction(InstructionSuccessorType linkToNextType, CfgInstruction returnInstruction, ICfgNode next) {
        ICfgNode resolvedForRet = LinkCfgInstructionWithType(linkToNextType, returnInstruction, next);
        // Link the call instruction now that ret target is known
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
    private ICfgNode LinkCfgInstructionWithType(InstructionSuccessorType linkToNextType, CfgInstruction current, ICfgNode next) {
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

    private void AttachNewLink(InstructionSuccessorType linkToNextType, CfgInstruction current, ICfgNode next) {
        AttachToNext(current, next);
        if (!current.SuccessorsPerType.TryGetValue(linkToNextType, out ISet<ICfgNode>? successorsForType)) {
            successorsForType = new HashSet<ICfgNode>();
            current.SuccessorsPerType[linkToNextType] = successorsForType;
        }
        successorsForType.Add(next);
    }

    private ICfgNode ResolveSuccessorConflict(CfgInstruction current, ICfgNode next, ICfgNode shouldBeNext) {
        if (shouldBeNext is SelectorNode selectorNode) {
            LinkSelectorNode(selectorNode, next);
            return selectorNode;
        }
        return CreateSelectorNodeBetween((CfgInstruction)shouldBeNext, (CfgInstruction)next);
    }

    /// <summary>
    /// Creates a <see cref="SelectorNode"/> at the address of the two instructions and inserts it
    /// as an intermediate predecessor of both, dispatching between their two distinct
    /// <see cref="CfgInstruction.Signature"/>s based on memory bytes at runtime.
    /// </summary>
    /// <remarks>
    /// The order of the two <see cref="InsertIntermediatePredecessor"/> calls matters:
    /// the first call is the only opportunity for the selector's incoming edges
    /// (predecessor → selector) to be wired and reconciled, which gives the selector a
    /// containing block. The second call assumes the selector already has a block.
    /// Therefore we always process the instruction that has predecessors FIRST, guaranteeing
    /// that at least one predecessor → selector edge is wired before the first
    /// selector → variant edge fires.
    /// </remarks>
    public SelectorNode CreateSelectorNodeBetween(CfgInstruction instruction1, CfgInstruction instruction2) {
        SelectorNode selectorNode = new SelectorNode(_idAllocator.AllocateId(), instruction1.Address);
        _executionCompiler.Compile(selectorNode);
        // Wire the instruction that already has predecessors FIRST so the selector enters a block
        // via the predecessor -> selector edge before any selector -> variant edge fires.
        if (instruction1.Predecessors.Count > 0) {
            InsertIntermediatePredecessor(instruction1, selectorNode);
            InsertIntermediatePredecessor(instruction2, selectorNode);
        } else {
            InsertIntermediatePredecessor(instruction2, selectorNode);
            InsertIntermediatePredecessor(instruction1, selectorNode);
        }
        return selectorNode;
    }

    private InstructionSuccessorType ComputeSuccessorTypeForRet(CfgInstruction call, ICfgNode nextAfterRet) {
        if (call.NextInMemoryAddress32.ToSegmentedAddress() != nextAfterRet.Address) {
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
        if (!current.Successors.Add(next)) {
            // Edge already present — keep Link idempotent by short-circuiting before
            // any predecessor-set, unique-successor, or block-reconciliation side effect.
            return;
        }
        next.Predecessors.Add(current);
        // Unique successor is only valid if node has 1 successor
        current.UniqueSuccessor = current.MaxSuccessorsCount == 1 ? next : null;
        if (current.Successors.Count == current.MaxSuccessorsCount) {
            // We reached the max number of successors for this node
            // This means that there is no need to try to link it to other nodes, it is impossible there will be new links.
            current.CanHaveMoreSuccessors = false;
        }
        // Block-level reconciliation: single choke-point where block construction,
        // extension, splitting, and discovery state are kept in sync.
        ReconcileBlockAtEdge(current, next);
    }

    // -----------------------------------------------------------------------
    // Block reconciliation state machine.
    //
    // ReconcileBlockAtEdge is invoked after every instruction-level edge addition.
    // It is idempotent: a second call with the same (current, next) produces the
    // same final block layout as the first.
    //
    // Cases:
    //   Bootstrap: current has no containing block. Open one.
    //   Idempotency: edge is intra-block and next is current's neighbor; nothing to do.
    //   Intra-block non-neighbor: split at next (new entry) and/or after current (new terminator).
    //   Rear-side split: current is interior; split off the strict tail.
    //   Continuation: append next to current's block.
    //   Boundary: Close currentBlock and place next in its own block.
    //
    // Block reconciliation never mutates any instruction-level edge.
    // -----------------------------------------------------------------------

    private void ReconcileBlockAtEdge(ICfgNode current, ICfgNode next) {
        // Bootstrap: current has no containing block yet.
        if (current.ContainingBlock is null) {
            OpenBlock(current);
        }
        if (current.ContainingBlock is not CfgBlock currentBlock) {
            return;
        }

        // Intra-block edge: both nodes already share the same block.
        if (ReferenceEquals(next.ContainingBlock, currentBlock)) {
            int currentIndex = currentBlock.IndexOf(current);
            int nextIndex = currentBlock.IndexOf(next);
            // True idempotency: next immediately follows current in the block.
            if (currentIndex + 1 == nextIndex) {
                return;
            }
            // Non-neighbor intra-block edge: split at next so it becomes a block entry.
            if (nextIndex > 0) {
                SplitBlock(currentBlock, nextIndex, completePrefixDiscovery: true);
            }
            // After the split above current may now be in a different block.
            // If current is interior, split after it so it becomes a terminator.
            if (current.ContainingBlock is CfgBlock currentBlockAfterSplit) {
                int updatedCurrentIndex = currentBlockAfterSplit.IndexOf(current);
                if (updatedCurrentIndex >= 0 && updatedCurrentIndex < currentBlockAfterSplit.Instructions.Count - 1) {
                    SplitBlock(currentBlockAfterSplit, updatedCurrentIndex + 1, completePrefixDiscovery: true);
                }
            }
            return;
        }

        // Rear-side split: current is interior to its block so next must be in a different block.
        int indexOfCurrent = currentBlock.IndexOf(current);
        if (indexOfCurrent >= 0 && indexOfCurrent < currentBlock.Instructions.Count - 1) {
            SplitBlock(currentBlock, indexOfCurrent + 1, completePrefixDiscovery: false);
        }

        // Continuation check: Is it a simple fallthrough?
        // Uses IsBlockTerminator, IsBlockStarter, and static memory adjacency.
        // SelectorNode.IsBlockTerminator is always true, so a
        // SelectorNode current short-circuits to false.
        bool isContinuation =
            !current.IsBlockTerminator
            && !next.IsBlockStarter
            && current is CfgInstruction cfgCurrent
            && cfgCurrent.NextInMemoryAddress32.ToSegmentedAddress() == next.Address;

        // Continuation: append next to current's block.
        if (isContinuation && next.ContainingBlock is null) {
            AppendToBlock(currentBlock, next);
            if (next.IsBlockTerminator) {
                CompleteBlockDiscovery(currentBlock);
            }
            return;
        }

        // Boundary path: close currentBlock and place next.
        CompleteBlockDiscovery(currentBlock);
        AssignBlockForNext(next);
    }

    /// <summary>
    /// Opens a fresh <see cref="CfgBlock"/> seeded with <paramref name="entry"/> and
    /// auto-marks discovery-complete when the entry is itself a block terminator.
    /// </summary>
    private CfgBlock OpenBlock(ICfgNode entry) {
        CfgBlock block = new CfgBlock(_idAllocator.AllocateId(), entry);
        entry.ContainingBlock = block;
        if (entry.IsBlockTerminator) {
            CompleteBlockDiscovery(block);
        }
        return block;
    }

    /// <summary>
    /// Appends <paramref name="node"/> to <paramref name="block"/> and sets its containing block.
    /// </summary>
    private void AppendToBlock(CfgBlock block, ICfgNode node) {
        block.Append(node);
        node.ContainingBlock = block;
    }

    /// <summary>
    /// Monotonic flip false → true on <see cref="CfgBlock.IsDiscoveryComplete"/>;
    /// no-op when already complete.
    /// </summary>
    private static void CompleteBlockDiscovery(CfgBlock block) {
        if (!block.IsDiscoveryComplete) {
            block.IsDiscoveryComplete = true;
        }
    }

    /// <summary>
    /// Splits <paramref name="block"/> at <paramref name="splitIndex"/>, moving the tail
    /// into a new closed suffix block. Optionally closes the prefix.
    /// Touches no instruction-level edges.
    /// </summary>
    private CfgBlock SplitBlock(CfgBlock block, int splitIndex, bool completePrefixDiscovery) {
        List<ICfgNode> tail = block.SliceFrom(splitIndex);
        ICfgNode newEntry = tail[0];
        CfgBlock newBlock = new CfgBlock(_idAllocator.AllocateId(), newEntry);
        newEntry.ContainingBlock = newBlock;
        for (int i = 1; i < tail.Count; i++) {
            ICfgNode node = tail[i];
            newBlock.Append(node);
            node.ContainingBlock = newBlock;
        }
        newBlock.IsDiscoveryComplete = true;
        if (completePrefixDiscovery) {
            block.IsDiscoveryComplete = true;
        }
        return newBlock;
    }

    /// <summary>
    /// Places <paramref name="next"/> in a block: opens a fresh one if it has none,
    /// or splits its existing block when the new edge targets an interior node.
    /// No-op when <paramref name="next"/> is already the entry of its block.
    /// </summary>
    private void AssignBlockForNext(ICfgNode next) {
        if (next.ContainingBlock is null) {
            OpenBlock(next);
            return;
        }
        if (!ReferenceEquals(next, next.ContainingBlock.Entry)) {
            SplitBlock(next.ContainingBlock, next.ContainingBlock.IndexOf(next), completePrefixDiscovery: true);
        }
    }

    public override void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        // In-place block fix-up: replace oldInstruction in its block and transfer the
        // back-pointer BEFORE rewiring edges, so subsequent intra-block rewires hit
        // Idempotency in ReconcileBlockAtEdge and avoid spurious splits.
        CfgBlock? containingBlock = oldInstruction.ContainingBlock;
        if (containingBlock is not null) {
            int index = containingBlock.IndexOf(oldInstruction);
            if (index >= 0) {
                containingBlock.ReplaceInPlace(index, newInstruction);
                newInstruction.ContainingBlock = containingBlock;
                oldInstruction.ContainingBlock = null;
            }
        }
        SwitchPredecessorsToNew(oldInstruction, newInstruction);
        SwitchSuccessorsToNew(oldInstruction, newInstruction);
        // Keep data from SuccessorsPerType map of old
        ReplaceSuccessorsPerType(oldInstruction, newInstruction);
        // Update caches
        newInstruction.UpdateSuccessorCache();
        oldInstruction.UpdateSuccessorCache();
    }

    /// <summary>
    /// Inserts newPredecessor as an intermediate node between current and its own predecessors.
    /// </summary>
    public void InsertIntermediatePredecessor(ICfgNode current, ICfgNode newPredecessor) {
        SwitchPredecessorsToNew(current, newPredecessor);
        // Make new the only predecessor of current
        current.Predecessors.Clear();
        LinkToNext(newPredecessor, current);
        newPredecessor.UpdateSuccessorCache();
    }

    private void SwitchSuccessorsToNew(ICfgNode oldNode, ICfgNode newNode) {
        foreach (ICfgNode successor in oldNode.Successors) {
            // Replace current with new in the successor's predecessors
            LinkToNext(newNode, successor);
            successor.Predecessors.Remove(oldNode);
            // No cache update of newNode. Should be done at the end of the loop but caller already does it.
        }
        oldNode.Successors.Clear();
    }

    private void SwitchPredecessorsToNew(ICfgNode oldNode, ICfgNode newNode) {
        foreach (ICfgNode predecessor in oldNode.Predecessors) {
            // Replace current with new in the predecessors successors
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
