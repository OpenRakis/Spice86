namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A <c>//</c> comment line in the generated code.</summary>
/// <param name="Text">The comment text, printed after <c>//</c>.</param>
internal sealed record CommentNode(string Text) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitCommentNode(this);
}
