namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A cast the generator adds; prints exactly like <see cref="CSharpExpressionPrinter.Cast(DataType, CSharpFragment)"/>, without folding constants.</summary>
/// <param name="DataType">The target type.</param>
/// <param name="Value">The value to cast.</param>
internal sealed record ExplicitCastNode(DataType DataType, IVisitableAstNode Value) : ValueNode(DataType) {
    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitExplicitCastNode(this);
}
