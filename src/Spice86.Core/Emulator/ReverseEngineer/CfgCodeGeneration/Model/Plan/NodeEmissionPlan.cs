namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Per-node emission decisions for a method, precomputed so the method emitter does not recompute
/// the label or block-entry status while writing.
/// </summary>
/// <param name="Node">The CFG node to emit.</param>
/// <param name="Block">The block this node belongs to.</param>
/// <param name="Label">The label for this node, used as a goto target.</param>
/// <param name="IsBlockEntry">True for the block's first instruction; drives the label and per-block event check.</param>
internal sealed record NodeEmissionPlan(ICfgNode Node, CfgBlock Block, string Label, bool IsBlockEntry);
