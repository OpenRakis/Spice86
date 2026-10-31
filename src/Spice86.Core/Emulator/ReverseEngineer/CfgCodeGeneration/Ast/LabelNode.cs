namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The label a same-method <c>goto</c> can jump to.</summary>
/// <param name="Target">The target the label is attached to.</param>
internal sealed record LabelNode(IGotoTarget Target) : IVisitableAstNode {
    /// <summary>A label for a CFG node of the same method.</summary>
    public LabelNode(ICfgNode target) : this(new CfgNodeGotoTarget(target)) {
    }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitLabelNode(this);
}
