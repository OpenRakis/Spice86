namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>The name of the generated field holding an instruction's signature bytes.</summary>
/// <param name="FieldName">The field name, printed as an identifier.</param>
internal sealed record SignatureFieldNode(string FieldName) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitSignatureFieldNode(this);
}
