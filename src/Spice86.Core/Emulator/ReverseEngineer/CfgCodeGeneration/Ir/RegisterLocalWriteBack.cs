namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>One pending write-back: the boundary of <see cref="Instruction"/> that holds
/// <see cref="Anchor"/> takes <see cref="Node"/> after its <see cref="InstructionStartNode"/>.</summary>
internal sealed record RegisterLocalWriteBack(ICfgNode Instruction, IVisitableAstNode Anchor,
    RegisterWriteBackNode Node);
