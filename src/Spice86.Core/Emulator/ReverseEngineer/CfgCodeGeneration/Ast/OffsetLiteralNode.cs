namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A 16-bit offset printed as a hex literal, such as <c>0x0010</c>.</summary>
/// <param name="Value">The offset value.</param>
internal sealed record OffsetLiteralNode(int Value) : ValueNode(DataType.UINT16) {
    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitOffsetLiteralNode(this);
}
