namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// A <c>Stack</c> helper call written as plain nodes, in the exact order the helper performs its memory
/// accesses and register commits. Each temp is declared once and is only used inside the expansion.
/// </summary>
/// <param name="Operands">One temp per call argument, in argument order, initialized with the argument;
/// the operand is bound before any stack pointer or memory state changes, as the helper's argument is.</param>
/// <param name="Statements">The expansion's statements after the operands: temporaries for the offsets
/// and loaded values, the memory accesses through <c>SegmentedPointerNode</c> on SS, the bare
/// <see cref="StackRangeCheckNode"/>s and the register assignments, in helper order.</param>
/// <param name="Result">The temp holding the value the call returns (pop, peek); <c>null</c> for the
/// void operations.</param>
internal sealed record StackExpansion(IReadOnlyList<VariableDeclarationNode> Operands,
    IReadOnlyList<IVisitableAstNode> Statements, VariableDeclarationNode? Result);
