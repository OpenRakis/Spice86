namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Wraps the traversal result with execution-context metadata. Returned only by
/// <see cref="CfgBlockGraphExporter.ExportFromExecutionContext"/>; the UI never sees this type directly.
/// </summary>
public sealed record CfgExecutionContextGraph {
    public required CfgBlockGraph Graph { get; init; }
    public required int CurrentContextDepth { get; init; }
    public required string CurrentContextEntryPoint { get; init; }
    public required string[] EntryPointAddresses { get; init; }
    public required ICfgNode? LastExecuted { get; init; }
    public required CfgBlock? LastExecutedBlock { get; init; }
}
