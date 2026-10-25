namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

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
        HashSet<ICfgNode> targeted = [.. Gotos(body).Select(jump => jump.Target)];
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
        && !ContainsNode<VariableDeclarationNode>(block.Body)
        && !ContainsNode<GotoNode>(block.Body)
        && !CompletesNormallyVisitor.CompletesNormally(block.Body)
        && !block.Block.Instructions.Any(entryNodes.Contains)
        && loops.LoopWithHeader(block.Block) is null;

    private static BlockNode RewriteBlock(BlockNode block, IReadOnlyDictionary<ICfgNode, LoweredBlock> kept,
        HashSet<ICfgNode> keptInstructions) {
        List<IVisitableAstNode> statements = [];
        foreach (IVisitableAstNode statement in block.Statements) {
            if (statement is GotoNode jump && kept.TryGetValue(jump.Target, out LoweredBlock? copy)) {
                statements.AddRange(StatementsOf(copy.Body));
                continue;
            }
            IVisitableAstNode rewritten = WithRewrittenBodies(statement, held => RewriteBlock(held, kept, keptInstructions));
            if (rewritten is InstructionBoundaryNode boundary && keptInstructions.Contains(boundary.Node)
                && statements.Count > 0 && !CompletesNormallyVisitor.CompletesNormally(statements[^1])) {
                continue;
            }
            statements.Add(rewritten);
        }
        return new BlockNode(statements.ToArray());
    }
}
