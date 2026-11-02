namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>A method entry: the node an entry reaches the method through and the block it starts.</summary>
/// <param name="Node">The entry node.</param>
/// <param name="Block">The block the entry starts.</param>
internal sealed record IrEntry(ICfgNode Node, IrBlock Block);
