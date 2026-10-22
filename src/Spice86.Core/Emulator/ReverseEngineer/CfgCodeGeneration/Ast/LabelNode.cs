namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The label a same-method <c>goto</c> can jump to.</summary>
/// <param name="Target">The node the label is attached to.</param>
internal sealed record LabelNode(ICfgNode Target) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitLabelNode(this);
}
