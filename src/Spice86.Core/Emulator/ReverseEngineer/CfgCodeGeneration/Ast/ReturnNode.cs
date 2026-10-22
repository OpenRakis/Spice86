namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A <c>return</c> of an expression, such as the <c>Action</c> a generated method returns.</summary>
/// <param name="Value">The expression the method returns.</param>
internal sealed record ReturnNode(IVisitableAstNode Value) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitReturnNode(this);
}
