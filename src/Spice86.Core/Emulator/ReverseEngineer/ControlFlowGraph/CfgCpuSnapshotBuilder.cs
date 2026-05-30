namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Interfaces;

/// <summary>
/// Single owner of "export the CFG block graph and partition it". Produces a <see cref="CfgCpuSnapshot"/>
/// that both the JSON exporter and the C# generator consume, so the export and partition passes run once and
/// the two outputs cannot diverge. A truncated graph yields a snapshot with no partitioned program.
/// </summary>
internal sealed class CfgCpuSnapshotBuilder {
    private readonly CfgBlockGraphExporter _graphExporter;
    private readonly CfgFunctionPartitioner _functionPartitioner;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ILoggerService _loggerService;

    public CfgCpuSnapshotBuilder(CfgBlockGraphExporter graphExporter, CfgFunctionPartitioner functionPartitioner,
        FunctionCatalogue functionCatalogue, ILoggerService loggerService) {
        _graphExporter = graphExporter;
        _functionPartitioner = functionPartitioner;
        _functionCatalogue = functionCatalogue;
        _loggerService = loggerService;
    }

    public CfgCpuSnapshot Build(ExecutionContextManager contextManager, int? nodeLimit) {
        CfgExecutionContextGraph exported = _graphExporter.ExportFromExecutionContext(contextManager, nodeLimit);
        if (exported.Graph.Truncated) {
            return new CfgCpuSnapshot { Exported = exported };
        }

        CfgPartitionedProgram? program = TryPartition(exported, contextManager);
        return new CfgCpuSnapshot { Exported = exported, PartitionedProgram = program };
    }

    /// <summary>
    /// Partitions the exported block graph, swallowing <see cref="InvalidOperationException"/> so a partition
    /// invariant violation (no roots, ownerless blocks) does not abort the whole state dump. The block graph
    /// is still valuable on its own (it is what reload consumes and what an engineer inspects to diagnose the
    /// failure), so a partitioning failure degrades to a blocks-only snapshot instead of propagating. The
    /// failure is logged at error level with the full detail the partitioner provides.
    /// </summary>
    private CfgPartitionedProgram? TryPartition(CfgExecutionContextGraph exported, ExecutionContextManager contextManager) {
        try {
            return _functionPartitioner.Partition(exported.Graph, contextManager, _functionCatalogue);
        } catch (InvalidOperationException partitioningFailure) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(partitioningFailure,
                    "CFG partitioning failed; dumping the block graph without partitions so the failure can be "
                    + "inspected. This indicates a CFG invariant violation: {Message}", partitioningFailure.Message);
            }
            return null;
        }
    }
}
