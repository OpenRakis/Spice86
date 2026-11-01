namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>What <see cref="RegisterLocalPromotion.Plan"/> decided for one method.</summary>
/// <param name="Reads">The node instances to print as a local: the read occurrences of every
/// promoted web plus its assignment targets, each with its printed text and fragment type.</param>
/// <param name="Declarations">The local declarations to print at the top of the method.</param>
/// <param name="WriteBacks">The write-backs to insert into the printed body.</param>
/// <param name="Dropped">The definition statement nodes of dropped webs to remove from the
/// printed body (by reference).</param>
internal sealed record RegisterLocalPlan(
    IReadOnlyDictionary<IVisitableAstNode, SubstitutedRead> Reads,
    IReadOnlyList<LocalDeclarationNode> Declarations,
    IReadOnlyList<RegisterLocalWriteBack> WriteBacks,
    IReadOnlySet<IVisitableAstNode> Dropped);
