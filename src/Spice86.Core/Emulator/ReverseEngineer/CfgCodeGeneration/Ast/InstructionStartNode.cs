namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>Marks the start of an instruction inside a method; prints <c>// <paramref name="Comment"/></c> like <see cref="CommentNode"/>.</summary>
/// <param name="Comment">The comment text, printed after <c>//</c>.</param>
internal sealed record InstructionStartNode(string Comment) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitInstructionStartNode(this);
}
