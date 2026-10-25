namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The lowered code of one CFG block, ready to be placed by <see cref="MethodStructurizer"/>.</summary>
/// <param name="Block">The CFG block.</param>
/// <param name="HasEntryCheck">Whether the block starts with a <c>CheckExternalEvents</c> call.</param>
/// <param name="Body">The instruction boundaries of the block, in instruction order, each ending with its explicit transfers.</param>
internal sealed record LoweredBlock(CfgBlock Block, bool HasEntryCheck, BlockNode Body);
