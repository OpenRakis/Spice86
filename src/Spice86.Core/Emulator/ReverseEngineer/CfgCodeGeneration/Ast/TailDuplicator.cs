namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using System.Collections.Generic;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Replaces every <c>goto</c> to a tiny diverging block by a copy of the block's statements: a speculative stub that
/// throws (<c>VerifySpeculativeEntryOrFail</c> then <c>FailAsUntested</c>) or a short epilogue is pasted at each of its
/// <c>goto</c>s, so <c>if (c) { goto L_X; }</c> becomes <c>if (c) { ...; throw FailAsUntested(...); }</c> and the label
/// disappears. A block is duplicable when it has at most 3 instruction boundaries, declares no local variable and holds
/// no <c>goto</c>, never completes normally, holds no partition entry and is not a loop header. The block's own
/// statements are removed from their original place when the statement right before them never completes normally
/// (they are unreachable); otherwise they stay, without a label because nothing targets them.
/// One pass, after the fallthrough <c>goto</c>s were removed; copies are not re-examined.
/// </summary>
internal static class TailDuplicator {
    /// <summary>
    /// <paramref name="body"/> where every <c>goto</c> to a duplicable block of <paramref name="blocks"/> is replaced
    /// by that block's statements.
    /// </summary>
    /// <param name="blocks">The lowered blocks the method was structured from.</param>
    /// <param name="entryNodes">The partition entry nodes of the method.</param>
    /// <param name="loops">The natural loops of the method's block graph.</param>
    public static BlockNode Apply(BlockNode body, IReadOnlyList<LoweredBlock> blocks, IReadOnlySet<ICfgNode> entryNodes,
        NaturalLoopForest loops) {
        Dictionary<ICfgNode, LoweredBlock> duplicableByEntry = new();
        foreach (LoweredBlock block in blocks.Where(block => IsDuplicable(block, entryNodes, loops))) {
            duplicableByEntry[block.Block.Entry] = block;
        }
        HashSet<ICfgNode> targeted = [];
        EachGoto(body, jump => targeted.Add(jump.Target));
        Dictionary<ICfgNode, LoweredBlock> kept = duplicableByEntry
            .Where(pair => targeted.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (kept.Count == 0) {
            return body;
        }
        HashSet<ICfgNode> keptInstructions = [.. kept.Values.SelectMany(block => block.Block.Instructions)];
        return RewriteBlock(body, kept, keptInstructions);
    }

    private static bool IsDuplicable(LoweredBlock block, IReadOnlySet<ICfgNode> entryNodes, NaturalLoopForest loops) =>
        block.Body.Statements.OfType<InstructionBoundaryNode>().Count() <= 3
        && !Contains<VariableDeclarationNode>(block.Body)
        && !Contains<GotoNode>(block.Body)
        && !CompletesNormallyVisitor.CompletesNormally(block.Body)
        && !block.Block.Instructions.Any(entryNodes.Contains)
        && loops.LoopWithHeader(block.Block) is null;

    private static bool Contains<T>(IVisitableAstNode node) {
        switch (node) {
            case T:
                return true;
            case BlockNode block:
                return block.Statements.Any(Contains<T>);
            case InstructionBoundaryNode boundary:
                return Contains<T>(boundary.Body);
            case IfElseNode ifElse:
                return Contains<T>(ifElse.TrueCase) || Contains<T>(ifElse.FalseCase);
            case WhileNode whileNode:
                return Contains<T>(whileNode.Body);
            case InfiniteLoopNode infiniteLoop:
                return Contains<T>(infiniteLoop.Body);
            case DoWhileNode doWhile:
                return Contains<T>(doWhile.Body);
            case SwitchNode switchNode:
                return switchNode.Sections.Any(section => Contains<T>(section.Body)) || Contains<T>(switchNode.Default);
            case CpuFaultTryCatchNode tryCatch:
                return Contains<T>(tryCatch.Body);
            default:
                return false;
        }
    }

    private static void EachGoto(IVisitableAstNode node, Action<GotoNode> action) {
        switch (node) {
            case GotoNode jump:
                action(jump);
                return;
            case BlockNode block:
                foreach (IVisitableAstNode statement in block.Statements) {
                    EachGoto(statement, action);
                }
                return;
            case InstructionBoundaryNode boundary:
                EachGoto(boundary.Body, action);
                return;
            case IfElseNode ifElse:
                EachGoto(ifElse.TrueCase, action);
                EachGoto(ifElse.FalseCase, action);
                return;
            case WhileNode whileNode:
                EachGoto(whileNode.Body, action);
                return;
            case InfiniteLoopNode infiniteLoop:
                EachGoto(infiniteLoop.Body, action);
                return;
            case DoWhileNode doWhile:
                EachGoto(doWhile.Body, action);
                return;
            case SwitchNode switchNode:
                foreach (SwitchSection section in switchNode.Sections) {
                    EachGoto(section.Body, action);
                }
                EachGoto(switchNode.Default, action);
                return;
            case CpuFaultTryCatchNode tryCatch:
                EachGoto(tryCatch.Body, action);
                return;
        }
    }

    private static BlockNode RewriteBlock(BlockNode block, IReadOnlyDictionary<ICfgNode, LoweredBlock> kept,
        HashSet<ICfgNode> keptInstructions) {
        List<IVisitableAstNode> statements = [];
        foreach (IVisitableAstNode statement in block.Statements) {
            if (statement is GotoNode jump && kept.TryGetValue(jump.Target, out LoweredBlock? copy)) {
                statements.AddRange(Sequence(copy.Body).Statements);
                continue;
            }
            IVisitableAstNode rewritten = RewriteStatement(statement, kept, keptInstructions);
            if (rewritten is InstructionBoundaryNode boundary && keptInstructions.Contains(boundary.Node)
                && statements.Count > 0 && !CompletesNormallyVisitor.CompletesNormally(statements[^1])) {
                continue;
            }
            statements.Add(rewritten);
        }
        return new BlockNode(statements.ToArray());
    }

    private static IVisitableAstNode RewriteStatement(IVisitableAstNode statement,
        IReadOnlyDictionary<ICfgNode, LoweredBlock> kept, HashSet<ICfgNode> keptInstructions) {
        switch (statement) {
            case BlockNode block:
                return RewriteBlock(block, kept, keptInstructions);
            case InstructionBoundaryNode boundary:
                return boundary with { Body = RewriteBlock(boundary.Body, kept, keptInstructions) };
            case IfElseNode ifElse:
                return new IfElseNode(ifElse.Condition, RewriteBlock(Sequence(ifElse.TrueCase), kept, keptInstructions),
                    RewriteBlock(Sequence(ifElse.FalseCase), kept, keptInstructions));
            case WhileNode whileNode:
                return new WhileNode(whileNode.Condition, RewriteBlock(whileNode.Body, kept, keptInstructions));
            case InfiniteLoopNode infiniteLoop:
                return infiniteLoop with { Body = RewriteBlock(infiniteLoop.Body, kept, keptInstructions) };
            case DoWhileNode doWhile:
                return doWhile with { Body = RewriteBlock(doWhile.Body, kept, keptInstructions) };
            case SwitchNode switchNode:
                return switchNode with {
                    Sections = switchNode.Sections
                        .Select(section => section with { Body = RewriteBlock(section.Body, kept, keptInstructions) })
                        .ToList(),
                    Default = RewriteBlock(switchNode.Default, kept, keptInstructions)
                };
            case CpuFaultTryCatchNode tryCatch:
                return tryCatch with { Body = RewriteBlock(tryCatch.Body, kept, keptInstructions) };
            default:
                return statement;
        }
    }
}
