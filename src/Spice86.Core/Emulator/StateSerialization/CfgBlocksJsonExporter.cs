namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization.CfgReload;
using Spice86.Core.Emulator.StateSerialization.FunctionPartitioning;

using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON serialization adapter for the CFG block graph. Converts a <see cref="CfgCpuSnapshot"/> (the exported
/// graph plus its partition overlay, produced by <see cref="CfgCpuSnapshotBuilder"/>) into the
/// <see cref="CfgCpuGraph"/> / <see cref="CfgBlockInfo"/> wire model used for on-disk dumps and MCP responses.
/// This class no longer exports or partitions the graph itself; that single responsibility lives in
/// <see cref="CfgCpuSnapshotBuilder"/>.
/// </summary>
public class CfgBlocksJsonExporter {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AstInstructionRenderer _renderer;
    private readonly CfgCpuSnapshotBuilder _snapshotBuilder;
    private readonly CfgPartitionSerializationMapper _partitionMapper;

    /// <summary>
    /// Creates an exporter over the given snapshot builder.
    /// </summary>
    internal CfgBlocksJsonExporter(CfgCpuSnapshotBuilder snapshotBuilder) {
        _renderer = new AstInstructionRenderer(AsmRenderingConfig.CreateSpice86Style());
        _snapshotBuilder = snapshotBuilder;
        _partitionMapper = new CfgPartitionSerializationMapper();
    }

    /// <summary>
    /// Builds a snapshot from the execution context (convenience for callers that do not already have one,
    /// e.g. the MCP graph tool and tests), then maps it to the JSON wire model.
    /// </summary>
    internal CfgCpuGraph BuildGraph(ExecutionContextManager contextManager, int? nodeLimit) =>
        ToCfgCpuGraph(_snapshotBuilder.Build(contextManager, nodeLimit));

    /// <summary>
    /// Maps an already-built snapshot to the JSON wire model.
    /// </summary>
    internal CfgCpuGraph ToCfgCpuGraph(CfgCpuSnapshot snapshot) {
        CfgExecutionContextGraph exported = snapshot.Exported;
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
        if (snapshot.PartitionedProgram is CfgPartitionedProgram program) {
            partitioning = _partitionMapper.Map(program);
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
            PartitioningRequiresFullGraph = graph.Truncated ? true : null,
            Truncated = graph.Truncated
        };
    }

    /// <summary>
    /// Maps an already-built snapshot to the blocks-only on-disk view (no partition overlay).
    /// </summary>
    internal CfgBlocksDump ToBlocksDump(CfgCpuSnapshot snapshot) {
        CfgExecutionContextGraph exported = snapshot.Exported;
        CfgBlockGraph graph = exported.Graph;

        HashSet<int> includedBlockIds = new(graph.Blocks.Select(n => n.Block.Id));
        CfgBlockInfo[] blocks = graph.Blocks
            .Select(graphNode => BuildCfgBlockInfo(graphNode.Block, includedBlockIds))
            .ToArray();

        int? lastExecutedBlockId = null;
        if (exported.LastExecutedBlock is CfgBlock lastBlock) {
            lastExecutedBlockId = lastBlock.Id;
        }

        return new CfgBlocksDump {
            CurrentContextDepth = exported.CurrentContextDepth,
            CurrentContextEntryPoint = exported.CurrentContextEntryPoint,
            TotalEntryPoints = exported.EntryPointAddresses.Length,
            EntryPointAddresses = exported.EntryPointAddresses,
            LastExecutedAddress = exported.LastExecuted?.Address.ToString(),
            LastExecutedBlockId = lastExecutedBlockId,
            Blocks = blocks,
            Truncated = graph.Truncated
        };
    }

    /// <summary>
    /// Maps an already-built snapshot to the partition overlay on-disk view. A truncated graph or a partitioning
    /// failure yields an empty overlay (partitioning requires the full graph and does not throw out here).
    /// </summary>
    internal CfgPartitionsDump ToPartitionsDump(CfgCpuSnapshot snapshot) {
        CfgFunctionPartitioningResult? partitioning = null;
        if (snapshot.PartitionedProgram is CfgPartitionedProgram program) {
            partitioning = _partitionMapper.Map(program);
        }

        return new CfgPartitionsDump {
            Partitions = partitioning?.Partitions ?? [],
            Transfers = partitioning?.Transfers ?? [],
            PartitioningRequiresFullGraph = snapshot.Exported.Graph.Truncated ? true : null
        };
    }

    /// <summary>
    /// Serializes an already-built snapshot to a JSON string.
    /// </summary>
    internal string ToJson(CfgCpuSnapshot snapshot) {
        return JsonSerializer.Serialize(ToCfgCpuGraph(snapshot), SerializerOptions);
    }

    /// <summary>
    /// Writes the blocks-only JSON for an already-built snapshot to a file.
    /// </summary>
    internal void WriteBlocks(CfgCpuSnapshot snapshot, string path) {
        File.WriteAllText(path, JsonSerializer.Serialize(ToBlocksDump(snapshot), SerializerOptions));
    }

    /// <summary>
    /// Writes the partition overlay JSON for an already-built snapshot to a file.
    /// </summary>
    internal void WritePartitions(CfgCpuSnapshot snapshot, string path) {
        File.WriteAllText(path, JsonSerializer.Serialize(ToPartitionsDump(snapshot), SerializerOptions));
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
