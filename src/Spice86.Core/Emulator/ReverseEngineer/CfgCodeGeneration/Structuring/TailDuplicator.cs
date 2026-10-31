namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using System.Collections.Generic;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Replaces every <c>goto</c> to a tiny diverging block by a copy of the block's statements: a speculative stub that
/// throws (<c>VerifySpeculativeEntryOrFail</c> then <c>FailAsUntested</c>) or a short epilogue is pasted at each of its
/// <c>goto</c>s, so <c>if (c) { goto L_X; }</c> becomes <c>if (c) { ...; throw FailAsUntested(...); }</c> and the label
/// disappears. A block is duplicable when it has at most 3 instruction boundaries, declares no local variable and holds
/// no <c>goto</c>, never completes normally, holds no method entry and is not a loop header. The block's own
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
    /// <param name="loops">The natural loops of the method's block graph.</param>
    public static BlockNode Apply<TBlock>(BlockNode body, IReadOnlyList<LoweredBlock<TBlock>> blocks,
        NaturalLoopForest<TBlock> loops) where TBlock : class {
        Dictionary<IGotoTarget, LoweredBlock<TBlock>> duplicableByEntry = new();
        foreach (LoweredBlock<TBlock> block in blocks.Where(block => IsDuplicable(block, loops))) {
            duplicableByEntry[block.Entry] = block;
        }
        HashSet<IGotoTarget> targeted = [.. Gotos(body).Select(jump => jump.Target)];
        Dictionary<IGotoTarget, LoweredBlock<TBlock>> kept = duplicableByEntry
            .Where(pair => targeted.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (kept.Count == 0) {
            return body;
        }
        HashSet<ICfgNode> keptInstructions = [.. kept.Values.SelectMany(BoundaryNodes)];
        AstResult<BlockNode> result = new();
        AstTrampoline.Run(RewriteBlockIt(body, kept, keptInstructions, result));
        return result.Value!;
    }

    private static IEnumerable<ICfgNode> BoundaryNodes<TBlock>(LoweredBlock<TBlock> block) where TBlock : class =>
        block.Body.Statements.OfType<InstructionBoundaryNode>().Select(boundary => boundary.Node);

    private static bool IsDuplicable<TBlock>(LoweredBlock<TBlock> block, NaturalLoopForest<TBlock> loops)
        where TBlock : class {
        int boundaries = block.Body.Statements.OfType<InstructionBoundaryNode>().Count();
        bool locals = ContainsNode<VariableDeclarationNode>(block.Body);
        bool gotos = ContainsNode<GotoNode>(block.Body);
        bool completes = CompletesNormallyVisitor.CompletesNormally(block.Body);
        bool loopHeader = loops.LoopWithHeader(block.Block) is not null;
        return boundaries <= 3 && !locals && !gotos && !SpansMultipleInstructions(block.Block)
            && !completes && !block.HoldsEntry && !loopHeader;
    }

    /// <summary>A block spanning several instructions flattens to plain sequential statements - the
    /// intra-block fallthrough holds no explicit <see cref="GotoNode"/> that would refuse duplication -
    /// so the check counts the distinct origin nodes the statements were lifted from (by reference: two
    /// plan nodes can share an address). A block lifted from more than one instruction is not
    /// duplicable.
    /// </summary>
    private static bool SpansMultipleInstructions<TBlock>(TBlock block) where TBlock : class =>
        block is IrBlock irBlock
        && irBlock.Statements.Select(statement => statement.Origin)
            .Distinct(ReferenceEqualityComparer.Instance).Skip(1).Any();

    private static IEnumerable<AstSubcall> RewriteBlockIt<TBlock>(BlockNode block,
        IReadOnlyDictionary<IGotoTarget, LoweredBlock<TBlock>> kept, HashSet<ICfgNode> keptInstructions,
        AstResult<BlockNode> result) where TBlock : class {
        List<IVisitableAstNode> statements = [];
        foreach (IVisitableAstNode statement in block.Statements) {
            if (statement is GotoNode jump && kept.TryGetValue(jump.Target, out LoweredBlock<TBlock>? copy)) {
                statements.AddRange(StatementsOf(copy.Body));
                continue;
            }
            AstResult<IVisitableAstNode> rewritten = new();
            yield return AstTrampoline.Call(WithRewrittenBodiesIt(statement,
                (held, child) => RewriteBlockIt(held, kept, keptInstructions, child), rewritten));
            if (rewritten.Value is InstructionBoundaryNode boundary && keptInstructions.Contains(boundary.Node)
                && statements.Count > 0 && !CompletesNormallyVisitor.CompletesNormally(statements[^1])) {
                continue;
            }
            statements.Add(rewritten.Value!);
        }
        result.Value = new BlockNode(statements.ToArray());
    }
}
