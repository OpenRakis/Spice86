namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The goto target of a lowered <c>CfgBlock</c> method: the CFG node the jump lands on.</summary>
/// <param name="Node">The CFG node.</param>
internal sealed record CfgNodeGotoTarget(ICfgNode Node) : IGotoTarget;
