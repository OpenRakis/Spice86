namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
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
internal sealed record RegisterLocalPlan(
    IReadOnlyDictionary<IVisitableAstNode, SubstitutedRead> Reads,
    IReadOnlyList<LocalDeclarationNode> Declarations,
    IReadOnlyList<RegisterLocalWriteBack> WriteBacks,
    IReadOnlySet<IVisitableAstNode> Dropped,
    IReadOnlyDictionary<IVisitableAstNode, IVisitableAstNode> Replacements,
    IReadOnlySet<TypedParameter> EntryParameters);
