namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A DIV/IDIV pattern lowered to one tuple assignment calling the matching helper.</summary>
/// <param name="Division">The recognized division: width, signedness, operands and output registers.</param>
internal sealed record DivisionNode(DivisionPattern Division) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitDivisionNode(this);
}
