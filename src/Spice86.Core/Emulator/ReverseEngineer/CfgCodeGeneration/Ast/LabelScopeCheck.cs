namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Collections.Immutable;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Checks that every <c>goto</c> of a method targets a label declared in a C# block that encloses the <c>goto</c>,
/// following the blocks the printer writes: <c>if</c> / <c>else</c> arms, loop bodies, a <c>switch</c> with all its
/// sections, and <c>try</c> bodies start a block; a statement sequence and an instruction boundary do not.
/// </summary>
internal static class LabelScopeCheck {
    /// <summary>Throws when a <c>goto</c> of <paramref name="method"/> cannot see its label.</summary>
    /// <param name="method">Everything printed at the top level of the method: the entry dispatch, then the body.</param>
    /// <exception cref="InvalidOperationException">A <c>goto</c> targets a label outside its enclosing blocks.</exception>
    public static void Verify(BlockNode method) => VerifyScope(method, ImmutableHashSet<ICfgNode>.Empty);

    private static void VerifyScope(IVisitableAstNode scope, ImmutableHashSet<ICfgNode> enclosing) =>
        VerifyStatement(scope, enclosing.Union(DeclaredLabels(scope)));

    private static void VerifyStatement(IVisitableAstNode statement, ImmutableHashSet<ICfgNode> visible) {
        switch (statement) {
            case GotoNode gotoNode:
                if (!visible.Contains(gotoNode.Target)) {
                    throw new InvalidOperationException(
                        $"A goto to {gotoNode.Target.Address} targets a label that is not declared in an enclosing block.");
                }
                return;
            case BlockNode block:
                foreach (IVisitableAstNode inner in block.Statements) {
                    VerifyStatement(inner, visible);
                }
                return;
            case InstructionBoundaryNode boundary:
                VerifyStatement(boundary.Body, visible);
                return;
            case IfElseNode ifElse:
                VerifyScope(ifElse.TrueCase, visible);
                VerifyScope(ifElse.FalseCase, visible);
                return;
            case WhileNode whileNode:
                VerifyScope(whileNode.Body, visible);
                return;
            case InfiniteLoopNode loop:
                VerifyScope(loop.Body, visible);
                return;
            case DoWhileNode doWhile:
                VerifyScope(doWhile.Body, visible);
                return;
            case CpuFaultTryCatchNode tryCatch:
                VerifyScope(tryCatch.Body, visible);
                return;
            case SwitchNode switchNode:
                VerifyScope(Sequence(switchNode.Sections.Select(section => section.Body).Append(switchNode.Default)), visible);
                return;
        }
    }

    private static IEnumerable<ICfgNode> DeclaredLabels(IVisitableAstNode node) => node switch {
        LabelNode label => new[] { label.Target },
        BlockNode block => block.Statements.SelectMany(DeclaredLabels),
        InstructionBoundaryNode boundary => DeclaredLabels(boundary.Body),
        _ => Enumerable.Empty<ICfgNode>()
    };
}
