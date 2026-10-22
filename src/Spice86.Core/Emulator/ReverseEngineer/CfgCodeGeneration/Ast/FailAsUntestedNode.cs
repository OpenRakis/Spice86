namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A <c>throw FailAsUntested(...)</c> line whose message is built from literal parts and value holes.</summary>
/// <param name="Parts">The message parts, in print order; a part with a value makes the message interpolated.</param>
internal sealed record FailAsUntestedNode(IReadOnlyList<UntestedMessagePart> Parts) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitFailAsUntestedNode(this);
}
