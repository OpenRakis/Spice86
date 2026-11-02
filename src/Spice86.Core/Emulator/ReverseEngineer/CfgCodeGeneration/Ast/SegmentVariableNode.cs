namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>The generated variable holding a segment value, such as <c>cs1</c>.</summary>
/// <param name="Segment">The segment value the variable stands for.</param>
internal sealed record SegmentVariableNode(ushort Segment) : ValueNode(DataType.UINT16) {
    /// <inheritdoc />
    public override T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitSegmentVariableNode(this);
}
