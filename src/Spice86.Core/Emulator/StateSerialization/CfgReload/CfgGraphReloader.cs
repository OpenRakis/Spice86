namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Linq;

/// <summary>
/// Reloads a previously dumped CFG instruction graph into live emulator state. Must run after CPU /
/// memory state load and <b>before</b> <c>CfgCpu.SignalEntry</c>, so resumed execution reconnects to
/// the reloaded graph instead of building a disconnected one.
///
/// Blocks are loaded directly from the dump (not re-derived through the linker), and instruction-level
/// edges are restored by directly populating each node's successor/predecessor sets and refreshing its
/// cache. Neither path invokes <c>NodeLinker.ReconcileBlockAtEdge</c>.
///
/// Liveness is deliberately NOT restored here: reloaded instructions start non-live. The live state of
/// a node is coupled to its membership in <c>CurrentInstructions</c> and to its memory-write
/// breakpoints, and <c>CurrentInstructions.SetAsCurrent</c> is the only thing that establishes all
/// three together. Marking a node live here without that coupling would create a live-but-unbreakpointed
/// node that self-modifying-code detection could never evict. Instead, reloaded instructions are
/// registered in <c>PreviousInstructions</c> only; the first time execution reaches one, the feeder
/// matches it against memory and promotes it via <c>SetAsCurrent</c> (live + breakpoint together).
/// </summary>
internal sealed class CfgGraphReloader {
    private readonly State _state;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly PreviousInstructions _previousInstructions;
    private readonly SequentialIdAllocator _idAllocator;
    private readonly CfgNodeExecutionCompiler _executionCompiler;

    public CfgGraphReloader(CfgCpu cfgCpu, State state, CfgNodeExecutionCompiler executionCompiler, SequentialIdAllocator idAllocator) {
        _state = state;
        _executionCompiler = executionCompiler;
        _executionContextManager = cfgCpu.ExecutionContextManager;
        _previousInstructions = cfgCpu.CfgNodeFeeder.InstructionsFeeder.PreviousInstructions;
        _idAllocator = idAllocator;
    }

    /// <summary>
    /// Reconstructs the graph described by <paramref name="dump"/> into live emulator state.
    /// </summary>
    public void Reload(CfgReloadDump dump) {
        CfgNodeReconstructor reconstructor = new(_state, _executionCompiler);

        // 1. Materialize all nodes with their dumped ids, compile each, build the id -> node map.
        Dictionary<int, ICfgNode> nodesById = new();
        foreach (CfgReloadNodeInfo nodeInfo in dump.Nodes) {
            ICfgNode node = reconstructor.Reconstruct(nodeInfo);
            nodesById[nodeInfo.Id] = node;
        }

        // 2. Force every reconstructed instruction non-live (they are born live). Execution promotes
        //    them to live via CurrentInstructions.SetAsCurrent on first encounter, which is the only
        //    path that also installs the memory-write breakpoints required for self-modifying-code
        //    eviction. This must run BEFORE blocks are built so each block's non-live counter
        //    initializes correctly (CfgBlock reads IsLive on Append). Selectors are always live and
        //    carry no breakpoints, so they are left as-is.
        foreach (ICfgNode node in nodesById.Values) {
            if (node is CfgInstruction instruction) {
                instruction.SetLive(false);
            }
        }

        // 3. Register every reconstructed instruction in PreviousInstructions (NOT CurrentInstructions),
        //    so resumed execution reuses reloaded nodes via memory matching instead of parsing anew.
        foreach (ICfgNode node in nodesById.Values) {
            if (node is CfgInstruction instruction) {
                _previousInstructions.AddInstructionInPrevious(instruction);
            }
        }

        // 4. Restore typed instruction-level edges.
        RestoreEdges(dump, nodesById);

        // 5. Build blocks directly from the dump.
        RestoreBlocks(dump, nodesById);

        // 6. Repopulate execution-context entry points from the dumped addresses.
        RestoreEntryPoints(dump, nodesById);

        // 7. Seed the live allocator past the highest reloaded id.
        _idAllocator.NextId = dump.IdAllocatorNext;

        // 8. LastExecuted / NodeToExecuteNextAccordingToGraph and the context stack stay reset (graph-only scope).
    }

    private void RestoreEdges(CfgReloadDump dump, Dictionary<int, ICfgNode> nodesById) {
        // Populate successor/predecessor sets for all edges first.
        foreach (CfgReloadEdgeInfo edge in dump.Edges) {
            ICfgNode from = ResolveNode(nodesById, edge.From);
            ICfgNode to = ResolveNode(nodesById, edge.To);
            from.Successors.Add(to);
            to.Predecessors.Add(from);
            if (from is CfgInstruction fromInstruction) {
                InstructionSuccessorType type = ParseSuccessorType(edge.Type);
                if (!fromInstruction.SuccessorsPerType.TryGetValue(type, out ISet<ICfgNode>? successorsForType)) {
                    successorsForType = new HashSet<ICfgNode>();
                    fromInstruction.SuccessorsPerType[type] = successorsForType;
                }
                successorsForType.Add(to);
            }
        }

        // Finalize successor state for ALL nodes, including terminators with zero outgoing edges.
        foreach (ICfgNode node in nodesById.Values) {
            FinalizeSuccessorState(node);
        }
    }

    /// <summary>
    /// Derives <c>UniqueSuccessor</c> / <c>CanHaveMoreSuccessors</c> from the restored
    /// <c>MaxSuccessorsCount</c> via the shared <see cref="SuccessorInvariant"/> (the same rule the live
    /// <c>NodeLinker</c> applies per edge), then refreshes the successor cache (instructions:
    /// <c>SuccessorsPerAddress</c>; selectors: <c>SuccessorsPerSignature</c>).
    /// </summary>
    private static void FinalizeSuccessorState(ICfgNode node) {
        SuccessorInvariant.Refresh(node);
        node.UpdateSuccessorCache();
    }

    private void RestoreBlocks(CfgReloadDump dump, Dictionary<int, ICfgNode> nodesById) {
        foreach (CfgReloadBlockInfo blockInfo in dump.Blocks) {
            if (blockInfo.Nodes.Length == 0) {
                throw new InvalidOperationException($"Reload block {blockInfo.Id} has no nodes");
            }
            ICfgNode entry = ResolveNode(nodesById, blockInfo.Nodes[0]);
            CfgBlock block = new(blockInfo.Id, entry);
            entry.ContainingBlock = block;
            for (int i = 1; i < blockInfo.Nodes.Length; i++) {
                ICfgNode node = ResolveNode(nodesById, blockInfo.Nodes[i]);
                block.Append(node);
                node.ContainingBlock = block;
            }
            block.IsDiscoveryComplete = blockInfo.DiscoveryComplete;
        }
    }

    private void RestoreEntryPoints(CfgReloadDump dump, Dictionary<int, ICfgNode> nodesById) {
        _executionContextManager.ExecutionContextEntryPoints.Clear();
        ILookup<SegmentedAddress, CfgInstruction> instructionsByAddress = nodesById.Values
            .OfType<CfgInstruction>()
            .ToLookup(instruction => instruction.Address);

        foreach (string entryPointAddress in dump.EntryPoints) {
            SegmentedAddress address = ReloadAddressParser.ParseOrThrow(entryPointAddress, "entry point");
            ISet<CfgInstruction> entries = new HashSet<CfgInstruction>(instructionsByAddress[address]);
            _executionContextManager.ExecutionContextEntryPoints[address] = entries;
        }
    }

    private static ICfgNode ResolveNode(Dictionary<int, ICfgNode> nodesById, int id) {
        if (!nodesById.TryGetValue(id, out ICfgNode? node)) {
            throw new InvalidOperationException($"Reload references unknown node id {id}");
        }
        return node;
    }

    private static InstructionSuccessorType ParseSuccessorType(string type) {
        if (!Enum.TryParse(type, out InstructionSuccessorType parsed)) {
            throw new InvalidOperationException($"Unknown instruction successor type '{type}'");
        }
        return parsed;
    }
}
