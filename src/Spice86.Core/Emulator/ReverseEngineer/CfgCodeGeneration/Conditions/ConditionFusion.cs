namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Conditions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The execution ASTs rewritten by condition fusion for one method; other instructions keep theirs.</summary>
internal sealed class ConditionFusion(IReadOnlyDictionary<ICfgNode, IVisitableAstNode> rewrittenAsts) {
    /// <summary>The AST to lower for <paramref name="node"/>: its rewritten AST, or its own execution AST.</summary>
    public IVisitableAstNode ExecutionAstOf(ICfgNode node) {
        if (rewrittenAsts.TryGetValue(node, out IVisitableAstNode? rewritten)) {
            return rewritten;
        }
        return node.ExecutionAst;
    }
}
