namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using System.Linq;

/// <summary>
/// An ALU call written as plain nodes: operand temps, a result temp and one assignment per status flag the call
/// writes. Each temp is declared once and is only used inside the expansion.
/// </summary>
/// <param name="Operands">One temp per call argument, in argument order, initialized with the argument; then a
/// <c>carry</c> temp for the operations that read CF.</param>
/// <param name="Result">The temp holding the value the call returns.</param>
/// <param name="FlagAssignments">The status flags the call writes, in FLAGS bit order.</param>
internal sealed record AluExpansion(IReadOnlyList<VariableDeclarationNode> Operands, VariableDeclarationNode Result,
    IReadOnlyList<AluFlagAssignment> FlagAssignments) {
    /// <summary>The operand declarations, the result declaration, then the flag assignments.</summary>
    public IReadOnlyList<IVisitableAstNode> Statements =>
        [.. Operands, Result, .. FlagAssignments.Select(assignment => assignment.ToAssignment())];
}
