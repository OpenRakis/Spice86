namespace Spice86.Tests.Utility;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.StateSerialization;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Shared building/serialization of the CFG block graph for tests, so the graph comparison logic and
/// its JSON options live in one place instead of being duplicated across test classes.
/// </summary>
internal static class CfgBlocksTestJson {
    /// <summary>Serializer options matching the on-disk DumpedCfgBlocks fixtures.</summary>
    public static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Builds the block graph model from a live execution context.</summary>
    public static CfgCpuGraph BuildGraph(ExecutionContextManager contextManager) {
        CfgBlocksJsonExporter exporter = new(new CfgBlockGraphExporter(), new FunctionCatalogue(), new CfgFunctionPartitioner());
        return exporter.BuildGraph(contextManager, null);
    }

    /// <summary>Builds and serializes the block graph to its fixture JSON form.</summary>
    public static string Serialize(ExecutionContextManager contextManager) {
        return JsonSerializer.Serialize(BuildGraph(contextManager), Options);
    }
}
