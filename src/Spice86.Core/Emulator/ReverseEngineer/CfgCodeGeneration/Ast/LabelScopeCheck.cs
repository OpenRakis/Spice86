namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

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
    public static void Verify(BlockNode method) {
        Stack<(IVisitableAstNode Node, ImmutableHashSet<IGotoTarget> Visible)> pending = new();
        pending.Push((method, Scope(method, ImmutableHashSet<IGotoTarget>.Empty)));
        while (pending.Count > 0) {
            (IVisitableAstNode statement, ImmutableHashSet<IGotoTarget> visible) = pending.Pop();
            switch (statement) {
                case GotoNode gotoNode:
                    if (!visible.Contains(gotoNode.Target)) {
                        throw new InvalidOperationException(
                            $"A goto to {gotoNode.Target} targets a label that is not declared in an enclosing block.");
                    }
                    break;
                case BlockNode block:
                    for (int index = block.Statements.Count - 1; index >= 0; index--) {
                        pending.Push((block.Statements[index], visible));
                    }
                    break;
                case InstructionBoundaryNode boundary:
                    pending.Push((boundary.Body, visible));
                    break;
                case IfElseNode ifElse:
                    PushScope(pending, ifElse.FalseCase, visible);
                    PushScope(pending, ifElse.TrueCase, visible);
                    break;
                case WhileNode whileNode:
                    PushScope(pending, whileNode.Body, visible);
                    break;
                case PreTestLoopNode preTestLoop:
                    PushScope(pending, preTestLoop.Body, visible);
                    break;
                case InfiniteLoopNode loop:
                    PushScope(pending, loop.Body, visible);
                    break;
                case DoWhileNode doWhile:
                    PushScope(pending, doWhile.Body, visible);
                    break;
                case CpuFaultTryCatchNode tryCatch:
                    PushScope(pending, tryCatch.Body, visible);
                    break;
                case SwitchNode switchNode:
                    PushScope(pending,
                        Sequence(switchNode.Sections.Select(section => section.Body).Append(switchNode.Default)),
                        visible);
                    break;
            }
        }
    }

    private static void PushScope(Stack<(IVisitableAstNode, ImmutableHashSet<IGotoTarget>)> pending,
        IVisitableAstNode scope, ImmutableHashSet<IGotoTarget> enclosing) =>
        pending.Push((scope, Scope(scope, enclosing)));

    /// <summary>The labels visible inside <paramref name="scope"/>: its own declarations unioned with the enclosing set.</summary>
    private static ImmutableHashSet<IGotoTarget> Scope(IVisitableAstNode scope,
        ImmutableHashSet<IGotoTarget> enclosing) => enclosing.Union(DeclaredLabels(scope));

    private static List<IGotoTarget> DeclaredLabels(IVisitableAstNode node) {
        List<IGotoTarget> labels = [];
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            IVisitableAstNode current = pending.Pop();
            switch (current) {
                case LabelNode label:
                    labels.Add(label.Target);
                    break;
                case BlockNode block:
                    for (int index = block.Statements.Count - 1; index >= 0; index--) {
                        pending.Push(block.Statements[index]);
                    }
                    break;
                case InstructionBoundaryNode boundary:
                    pending.Push(boundary.Body);
                    break;
            }
        }
        return labels;
    }
}
