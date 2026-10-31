namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>A register swap printed as a C# tuple deconstruction: <c>(First, Second) = (Second, First);</c>.</summary>
/// <param name="First">The register receiving <see cref="Second"/>'s value.</param>
/// <param name="Second">The register receiving <see cref="First"/>'s value.</param>
internal sealed record TupleSwapNode(ValueNode First, ValueNode Second) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitTupleSwapNode(this);
}
