namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>A <c>goto</c> jumping to the label of a target node in the same generated method.</summary>
/// <param name="Target">The node whose label the <c>goto</c> jumps to.</param>
internal sealed record GotoNode(ICfgNode Target) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitGotoNode(this);
}
