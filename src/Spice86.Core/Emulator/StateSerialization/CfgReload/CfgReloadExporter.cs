namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>
/// Builds a <see cref="CfgReloadDump"/> from live emulator state by performing a full
/// instruction-level traversal of the CFG graph reachable from the execution-context entry points.
/// Mirrors <see cref="Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.CfgBlockGraphExporter"/>
/// but stays at the instruction level so individual variant nodes and their typed edges are captured.
/// </summary>
internal sealed class CfgReloadExporter {
    /// <summary>
    /// Traverses from <see cref="ExecutionContextManager.ExecutionContextEntryPoints"/> following both
    /// <c>Successors</c> and <c>Predecessors</c>, collecting every reachable instruction and selector
    /// node, their typed edges, the blocks that contain them, and the entry-point addresses.
    /// </summary>
    public CfgReloadDump Export(ExecutionContextManager contextManager) {
        HashSet<ICfgNode> reachable = TraverseReachableNodes(contextManager);

        List<CfgReloadNodeInfo> nodes = new();
        List<CfgReloadEdgeInfo> edges = new();
        HashSet<CfgBlock> blocks = new();
        int maxId = -1;

        foreach (ICfgNode node in reachable) {
            maxId = Math.Max(maxId, node.Id);
            switch (node) {
                case CfgInstruction instruction:
                    nodes.Add(BuildInstructionInfo(instruction));
                    CollectInstructionEdges(instruction, edges);
                    break;
                case SelectorNode selector:
                    nodes.Add(BuildSelectorInfo(selector));
                    CollectSelectorEdges(selector, edges);
                    break;
            }
            if (node.ContainingBlock is CfgBlock block) {
                blocks.Add(block);
            }
        }

        List<CfgReloadBlockInfo> blockInfos = new();
        foreach (CfgBlock block in blocks) {
            maxId = Math.Max(maxId, block.Id);
            blockInfos.Add(BuildBlockInfo(block));
        }

        string[] entryPoints = contextManager.ExecutionContextEntryPoints.Keys
            .Select(address => address.ToString())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        return new CfgReloadDump {
            IdAllocatorNext = maxId + 1,
            EntryPoints = entryPoints,
            Nodes = nodes.OrderBy(n => n.Id).ToArray(),
            Edges = edges
                .OrderBy(e => e.From).ThenBy(e => e.To).ThenBy(e => e.Type, StringComparer.Ordinal)
                .ToArray(),
            Blocks = blockInfos.OrderBy(b => b.Id).ToArray()
        };
    }

    private static HashSet<ICfgNode> TraverseReachableNodes(ExecutionContextManager contextManager) {
        IEnumerable<ICfgNode> seeds = contextManager.ExecutionContextEntryPoints.Values
            .SelectMany(entrySet => entrySet);
        // The reachable set is order-independent, so reuse the shared DFS helper. Neighbors span both
        // directions (Successors and Predecessors) to capture the whole connected component of each entry.
        return DepthFirstSearch
            .Enumerate(seeds, node => node.Successors.Concat(node.Predecessors))
            .ToHashSet();
    }

    private static CfgReloadNodeInfo BuildInstructionInfo(CfgInstruction instruction) {
        string sigHex = SigHex.Encode(instruction.Signature.SignatureValue);
        return new CfgReloadNodeInfo {
            Id = instruction.Id,
            Type = CfgReloadNodeType.Instruction,
            Addr = instruction.Address.ToString(),
            Bytes = sigHex,
            MaxSucc = instruction.MaxSuccessorsCount
        };
    }

    private static CfgReloadNodeInfo BuildSelectorInfo(SelectorNode selector) {
        return new CfgReloadNodeInfo {
            Id = selector.Id,
            Type = CfgReloadNodeType.Selector,
            Addr = selector.Address.ToString(),
            Bytes = null,
            MaxSucc = null
        };
    }

    private static void CollectInstructionEdges(CfgInstruction instruction, List<CfgReloadEdgeInfo> edges) {
        foreach (KeyValuePair<InstructionSuccessorType, ISet<ICfgNode>> entry in instruction.SuccessorsPerType) {
            foreach (ICfgNode successor in entry.Value) {
                edges.Add(new CfgReloadEdgeInfo {
                    From = instruction.Id,
                    To = successor.Id,
                    Type = entry.Key.ToString()
                });
            }
        }
    }

    private static void CollectSelectorEdges(SelectorNode selector, List<CfgReloadEdgeInfo> edges) {
        // A selector dispatches to instruction variants by signature; the dispatch table is rebuilt
        // from these edges on import via SelectorNode.UpdateSuccessorCache. The edge type is not
        // meaningful for a selector (it has no SuccessorsPerType), so Normal is used uniformly.
        foreach (ICfgNode successor in selector.Successors) {
            edges.Add(new CfgReloadEdgeInfo {
                From = selector.Id,
                To = successor.Id,
                Type = InstructionSuccessorType.Normal.ToString()
            });
        }
    }

    private static CfgReloadBlockInfo BuildBlockInfo(CfgBlock block) {
        return new CfgReloadBlockInfo {
            Id = block.Id,
            Nodes = block.Instructions.Select(node => node.Id).ToArray(),
            DiscoveryComplete = block.IsDiscoveryComplete
        };
    }
}
