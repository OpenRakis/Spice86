namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Per-node emission decisions for a method, precomputed so the method emitter does not recompute
/// the block-entry status while writing.
/// </summary>
/// <param name="Node">The CFG node to emit.</param>
/// <param name="Block">The block this node belongs to.</param>
/// <param name="IsBlockEntry">True for the block's first instruction.</param>
/// <param name="EmitsExternalEventCheck">True when this node emits a <c>CheckExternalEvents</c> call: the node is a method entry, or the entry of a loop back-edge target block.</param>
/// <param name="EmitsExternalEventCheckAfter">True when this instruction may enable interrupts (sti, popf, popfd) and its fallthrough does not land on a block entry that already checks; the check follows the instruction body, before the fallthrough transfer.</param>
internal sealed record NodeEmissionPlan(ICfgNode Node, CfgBlock Block, bool IsBlockEntry, bool EmitsExternalEventCheck, bool EmitsExternalEventCheckAfter);
