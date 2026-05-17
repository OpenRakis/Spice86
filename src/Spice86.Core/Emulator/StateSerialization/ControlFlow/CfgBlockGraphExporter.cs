namespace Spice86.Core.Emulator.StateSerialization.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Linq;

/// <summary>
/// Shared CFG block traversal layer. Owns BFS over blocks, seed collection, and edge discovery.
/// Both JSON serialization and UI rendering consume the exported graph without running their own traversal.
/// </summary>
public sealed class CfgBlockGraphExporter {
    /// <summary>
    /// Exports a <see cref="CfgExecutionContextGraph"/> from the given execution context manager,
    /// seeded by entry-point blocks and the last-executed block.
    /// </summary>
    public CfgExecutionContextGraph ExportFromExecutionContext(ExecutionContextManager contextManager, int? nodeLimit) {
        ExecutionContext currentContext = contextManager.CurrentExecutionContext;

        string[] entryPointAddresses = contextManager.ExecutionContextEntryPoints
            .Select(kvp => kvp.Key.ToString())
            .ToArray();

        List<CfgBlock> seeds = CollectSeeds(contextManager);

        ICfgNode? executingNode = contextManager.ExecutingNode;
        CfgBlock? executingBlock = executingNode?.ContainingBlock;

        CfgBlockGraph graph = TraverseFromSeeds(seeds, executingBlock, nodeLimit);

        ICfgNode? lastExecuted = currentContext.LastExecuted;
        CfgBlock? lastExecutedBlock = lastExecuted?.ContainingBlock;

        return new CfgExecutionContextGraph {
            Graph = graph,
            CurrentContextDepth = currentContext.Depth,
            CurrentContextEntryPoint = currentContext.EntryPoint.ToString(),
            EntryPointAddresses = entryPointAddresses,
            LastExecuted = lastExecuted,
            LastExecutedBlock = lastExecutedBlock
        };
    }

    /// <summary>
    /// Exports a <see cref="CfgBlockGraph"/> from a specific seed node, for UI navigation or search.
    /// </summary>
    public CfgBlockGraph ExportFromNode(ICfgNode startNode, ICfgNode? executingNode, int? nodeLimit) {
        CfgBlock? seedBlock = startNode as CfgBlock ?? startNode.ContainingBlock;
        if (seedBlock is null) {
            return new CfgBlockGraph {
                Blocks = [],
                Edges = [],
                Truncated = false
            };
        }

        CfgBlock? executingBlock = executingNode?.ContainingBlock;
        return TraverseFromSeeds([seedBlock], executingBlock, nodeLimit);
    }

    private static List<CfgBlock> CollectSeeds(ExecutionContextManager contextManager) {
        HashSet<int> seenBlockIds = new();
        List<CfgBlock> seeds = new();

        foreach (ISet<CfgInstruction> instructions in contextManager.ExecutionContextEntryPoints.Values) {
            foreach (CfgInstruction instruction in instructions) {
                CfgBlock? block = instruction.ContainingBlock;
                if (block != null && seenBlockIds.Add(block.Id)) {
                    seeds.Add(block);
                }
            }
        }

        if (contextManager.CurrentExecutionContext.LastExecuted is { } lastExecuted) {
            CfgBlock? lastBlock = lastExecuted.ContainingBlock;
            if (lastBlock != null && seenBlockIds.Add(lastBlock.Id)) {
                seeds.Add(lastBlock);
            }
        }

        return seeds.OrderBy(b => b.Entry.Address.Linear).ThenBy(b => b.Id).ToList();
    }

    private static CfgBlockGraph TraverseFromSeeds(List<CfgBlock> seeds, CfgBlock? executingBlock, int? nodeLimit) {
        HashSet<int> visitedBlockIds = new();
        HashSet<(int, int)> existingEdgeKeys = new();
        Queue<CfgBlock> queue = new();
        List<CfgBlockGraphNode> blocks = new();
        List<CfgBlockGraphEdge> edges = new();
        bool truncated = false;

        foreach (CfgBlock seed in seeds.Where(seed => visitedBlockIds.Add(seed.Id))) {
            queue.Enqueue(seed);
        }

        while (queue.Count > 0) {
            if (nodeLimit.HasValue && blocks.Count >= nodeLimit.Value) {
                truncated = true;
                break;
            }

            CfgBlock block = queue.Dequeue();

            bool isExecutingBlock = executingBlock is not null && block.Id == executingBlock.Id;
            blocks.Add(new CfgBlockGraphNode {
                Block = block,
                IsExecutingBlock = isExecutingBlock
            });

            // Successor edges
            foreach (ICfgNode successor in block.Successors) {
                CfgBlock? successorBlock = successor.ContainingBlock;
                if (successorBlock is null) {
                    continue;
                }
                (int, int) edgeKey = (block.Id, successorBlock.Id);
                if (existingEdgeKeys.Add(edgeKey)) {
                    edges.Add(new CfgBlockGraphEdge {
                        From = block,
                        To = successorBlock,
                        BridgeNode = successor
                    });
                }
                if (visitedBlockIds.Add(successorBlock.Id)) {
                    queue.Enqueue(successorBlock);
                }
            }

            // Predecessor edges
            foreach (CfgBlock? predecessorBlock in block.Predecessors.Select(predecessor => predecessor.ContainingBlock)) {
                if (predecessorBlock is null) {
                    continue;
                }
                (int, int) edgeKey = (predecessorBlock.Id, block.Id);
                if (existingEdgeKeys.Add(edgeKey)) {
                    edges.Add(new CfgBlockGraphEdge {
                        From = predecessorBlock,
                        To = block,
                        BridgeNode = block.Entry
                    });
                }
                if (visitedBlockIds.Add(predecessorBlock.Id)) {
                    queue.Enqueue(predecessorBlock);
                }
            }
        }

        if (queue.Count > 0) {
            truncated = true;
        }

        HashSet<int> includedIds = new(blocks.Select(n => n.Block.Id));
        CfgBlockGraphEdge[] closedEdges = edges
            .Where(e => includedIds.Contains(e.From.Id) && includedIds.Contains(e.To.Id))
            .ToArray();

        return new CfgBlockGraph {
            Blocks = blocks.ToArray(),
            Edges = closedEdges,
            Truncated = truncated
        };
    }
}
