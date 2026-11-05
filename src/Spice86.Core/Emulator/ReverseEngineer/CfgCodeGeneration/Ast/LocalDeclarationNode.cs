namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// A <c>type name;</c> declaration, printed before a CPU-fault run so the variable it
/// declares stays in scope after the <c>try</c> that holds its assignment. An
/// <paramref name="Initializer"/> prints <c>type name = initializer;</c> instead, for a variable
/// whose definite assignment may not depend on the structured control flow.
/// </summary>
/// <param name="DataType">The declared type.</param>
/// <param name="VariableName">The declared variable.</param>
/// <param name="Initializer">The initializing value, or none.</param>
internal sealed record LocalDeclarationNode(DataType DataType, string VariableName,
    ValueNode? Initializer) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitLocalDeclarationNode(this);
}
