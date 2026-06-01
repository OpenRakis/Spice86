namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.StateSerialization.CfgReload;
using Spice86.Core.Emulator.StateSerialization.FunctionPartitioning;

using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON serialization adapter for the CFG block graph. Delegates traversal to
/// <see cref="CfgBlockGraphExporter"/> and converts the exported graph into the
/// <see cref="CfgCpuGraph"/> / <see cref="CfgBlockInfo"/> wire model used for
/// on-disk dumps and MCP responses.
/// </summary>
public class CfgBlocksJsonExporter {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AstInstructionRenderer _renderer;
    private readonly CfgBlockGraphExporter _graphExporter;
    private readonly CfgFunctionPartitioner _functionPartitioner;
    private readonly CfgPartitionSerializationMapper _partitionMapper;
    private readonly FunctionCatalogue _functionCatalogue;

    /// <summary>
    /// Creates an exporter with a dedicated graph exporter instance and function labels.
    /// </summary>
    public CfgBlocksJsonExporter(CfgBlockGraphExporter graphExporter, FunctionCatalogue functionCatalogue, CfgFunctionPartitioner functionPartitioner) {
        _renderer = new AstInstructionRenderer(AsmRenderingConfig.CreateSpice86Style());
        _graphExporter = graphExporter;
        _functionPartitioner = functionPartitioner;
        _partitionMapper = new CfgPartitionSerializationMapper();
        _functionCatalogue = functionCatalogue;
    }

    /// <summary>
    /// Builds a <see cref="CfgCpuGraph"/> from the given execution context manager.
    /// Stops early if <paramref name="nodeLimit"/> is reached; <c>Truncated</c> is set accordingly.
    /// </summary>
    internal CfgCpuGraph BuildGraph(ExecutionContextManager contextManager, int? nodeLimit) {
        CfgExecutionContextGraph exported = _graphExporter.ExportFromExecutionContext(contextManager, nodeLimit);
        CfgBlockGraph graph = exported.Graph;

        HashSet<int> includedBlockIds = new(graph.Blocks.Select(n => n.Block.Id));
        CfgBlockInfo[] blocks = graph.Blocks
            .Select(graphNode => BuildCfgBlockInfo(graphNode.Block, includedBlockIds))
            .ToArray();

        int? lastExecutedBlockId = null;
        if (exported.LastExecutedBlock is CfgBlock lastBlock) {
            lastExecutedBlockId = lastBlock.Id;
        }

        CfgFunctionPartitioningResult? partitioning = null;
        bool? partitioningRequiresFullGraph = null;
        if (graph.Truncated) {
            partitioningRequiresFullGraph = true;
        } else {
            // Partitioning throws only when graph-structural invariants are violated (no roots, ownerless blocks).
            // Those invariants hold if the emulator CFG is correct, so a failure here means the emulator has a bug
            // and the graph data itself cannot be trusted. Letting the exception propagate is intentional.
            partitioning = _partitionMapper.Map(_functionPartitioner.Partition(graph, contextManager, _functionCatalogue));
        }

        return new CfgCpuGraph {
            CurrentContextDepth = exported.CurrentContextDepth,
            CurrentContextEntryPoint = exported.CurrentContextEntryPoint,
            TotalEntryPoints = exported.EntryPointAddresses.Length,
            EntryPointAddresses = exported.EntryPointAddresses,
            LastExecutedAddress = exported.LastExecuted?.Address.ToString(),
            LastExecutedBlockId = lastExecutedBlockId,
            Blocks = blocks,
            Partitions = partitioning?.Partitions,
            Transfers = partitioning?.Transfers,
            PartitioningRequiresFullGraph = partitioningRequiresFullGraph,
            Truncated = graph.Truncated
        };
    }

    /// <summary>
    /// Serializes the full block graph to a JSON string.
    /// </summary>
    internal string ToJson(ExecutionContextManager contextManager) {
        CfgCpuGraph graph = BuildGraph(contextManager, null);
        return JsonSerializer.Serialize(graph, SerializerOptions);
    }

    /// <summary>
    /// Writes the full block graph JSON to a file.
    /// </summary>
    internal void Write(ExecutionContextManager contextManager, string path) {
        string json = ToJson(contextManager);
        File.WriteAllText(path, json);
    }

    private CfgBlockInfo BuildCfgBlockInfo(CfgBlock block, HashSet<int> includedBlockIds) {
        List<string> asm = new(block.Instructions.Count);
        foreach (ICfgNode node in block.Instructions) {
            switch (node) {
                case CfgInstruction instruction:
                    string assembly = instruction.DisplayAst.Accept(_renderer);
                    string sigHex = SigHex.Encode(instruction.Signature.SignatureValue);
                    asm.Add($"{sigHex}|{assembly}");
                    break;
                case SelectorNode:
                    asm.Add("selector");
                    break;
            }
        }

        int[] predecessorBlockIds = block.Predecessors
            .Select(node => node.ContainingBlock)
            .OfType<CfgBlock>()
            .Select(containingBlock => containingBlock.Id)
            .Where(id => includedBlockIds.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        int[] successorBlockIds = block.Successors
            .Select(node => node.ContainingBlock)
            .OfType<CfgBlock>()
            .Select(containingBlock => containingBlock.Id)
            .Where(id => includedBlockIds.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        return new CfgBlockInfo {
            Id = block.Id,
            Entry = block.Entry.Address.ToString(),
            Dead = !block.IsLive ? true : null,
            Incomplete = !block.IsDiscoveryComplete ? true : null,
            Term = block.Terminator.Address.ToString(),
            Pred = predecessorBlockIds,
            Succ = successorBlockIds,
            Asm = asm.ToArray()
        };
    }
}
