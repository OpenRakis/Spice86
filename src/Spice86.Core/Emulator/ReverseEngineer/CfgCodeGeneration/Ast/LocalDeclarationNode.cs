namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>
/// A <c>type name;</c> declaration without initializer, printed before a CPU-fault run so the variable it
/// declares stays in scope after the <c>try</c> that holds its assignment.
/// </summary>
/// <param name="DataType">The declared type.</param>
/// <param name="VariableName">The declared variable.</param>
internal sealed record LocalDeclarationNode(DataType DataType, string VariableName) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitLocalDeclarationNode(this);
}
