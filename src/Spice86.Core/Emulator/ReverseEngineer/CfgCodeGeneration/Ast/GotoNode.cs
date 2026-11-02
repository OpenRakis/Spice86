namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>A <c>goto</c> jumping to the label of a target in the same generated method.</summary>
/// <param name="Target">The target whose label the <c>goto</c> jumps to.</param>
internal sealed record GotoNode(IGotoTarget Target) : IVisitableAstNode {
    /// <summary>A <c>goto</c> to the label of a CFG node of the same method.</summary>
    public GotoNode(ICfgNode target) : this(new CfgNodeGotoTarget(target)) {
    }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitGotoNode(this);
}
