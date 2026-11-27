namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;

/// <summary>What <see cref="RegisterLocalPromotion.Plan"/> decided for one method.</summary>
/// <param name="Reads">The node instances to print as a local: the read occurrences of every
/// promoted web plus its assignment targets, each with its printed text and fragment type.</param>
/// <param name="Declarations">The local declarations to print at the top of the method.</param>
/// <param name="WriteBacks">The write-backs to insert into the printed body.</param>
/// <param name="Dropped">The definition statement nodes of dropped webs to remove from the
/// printed body (by reference).</param>
/// <param name="Replacements">The node instances to replace by another node before printing
/// (by reference): a promoted pair's initial definitions.</param>
/// <param name="EntryParameters">The typed parameters whose entry-version web promoted: the
/// parameter already initializes the local, so the method emits no copy-in for it.</param>
/// <param name="CallOutputs">The printed left side of each typed call site (by
/// <see cref="ProcedureCallHelperNode"/> reference): one text per output element - <c>_</c>
/// for an output nobody reads live, or <c>null</c> to keep the register name.</param>
/// <param name="ReturnOutputs">The printed elements of each typed <c>return</c> (by
/// <see cref="ProcedureReturnHelperNode"/> reference): one text per output element in
/// signature order - the local that serves the exit's read of the output, or <c>null</c>
/// to keep the register name.</param>
/// <param name="ReturnExpressions">The expression each typed <c>return</c> prints for an output
/// element (by <see cref="ProcedureReturnHelperNode"/> reference): the merged expression of a
/// register assignment whose only live use was the exit's read of that output
/// (<see cref="Passes.IrExpressionPropagation.MergedIntoReturn"/>), or <c>null</c> to keep the
/// element's register name or local. <see cref="ReturnExpression.Type"/> casts the printed
/// expression to the output element's type, the same way an assignment right side casts to its
/// target.</param>
internal sealed record RegisterLocalPlan(
    IReadOnlyDictionary<IVisitableAstNode, SubstitutedRead> Reads,
    IReadOnlyList<LocalDeclarationNode> Declarations,
    IReadOnlyList<RegisterLocalWriteBack> WriteBacks,
    IReadOnlySet<IVisitableAstNode> Dropped,
    IReadOnlyDictionary<IVisitableAstNode, IVisitableAstNode> Replacements,
    IReadOnlySet<TypedParameter> EntryParameters,
    IReadOnlyDictionary<ProcedureCallHelperNode, IReadOnlyList<string?>> CallOutputs,
    IReadOnlyDictionary<ProcedureReturnHelperNode, IReadOnlyList<string?>> ReturnOutputs,
    IReadOnlyDictionary<ProcedureReturnHelperNode, IReadOnlyList<ReturnExpression?>> ReturnExpressions);

/// <summary>One merged <c>return</c> element: the expression and the output element's type to
/// print it as.</summary>
internal sealed record ReturnExpression(ValueNode Expression, DataType Type);
