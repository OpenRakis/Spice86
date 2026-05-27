namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Function-like code region over existing CFG blocks.
/// </summary>
internal sealed class CfgCodePartition {
    public required int Id { get; init; }
    public required CfgCodePartitionKind Kind { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<CfgBlock> Blocks { get; init; }
    public required IReadOnlyList<CfgCodePartitionEntry> Entries { get; init; }
}