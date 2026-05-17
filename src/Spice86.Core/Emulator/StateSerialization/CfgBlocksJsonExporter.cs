namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.StateSerialization.ControlFlow;

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

    /// <summary>
    /// Creates an exporter with a dedicated graph exporter instance.
    /// </summary>
    public CfgBlocksJsonExporter(CfgBlockGraphExporter graphExporter) {
        _renderer = new AstInstructionRenderer(AsmRenderingConfig.CreateSpice86Style());
        _graphExporter = graphExporter;
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

        return new CfgCpuGraph {
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
                    string sigHex = string.Concat(instruction.Signature.SignatureValue.Select(b => b.HasValue ? b.Value.ToString("X2") : "__"));
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
