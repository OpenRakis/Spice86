namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

/// <summary>A MUL/IMUL pattern lowered to one tuple assignment calling the matching helper.</summary>
/// <param name="Width">The operand width.</param>
/// <param name="Signed">Whether the operation is IMUL.</param>
/// <param name="Left">The first ALU argument.</param>
/// <param name="Right">The second ALU argument.</param>
/// <param name="Low">The register receiving the low half of the result.</param>
/// <param name="High">The register receiving the high half of the result.</param>
internal sealed record MultiplicationNode(BitWidth Width, bool Signed, ValueNode Left, ValueNode Right,
    RegisterNode Low, RegisterNode High) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) =>
        CodeGenerationNode.RequireVisitor(astVisitor, this).VisitMultiplicationNode(this);
}
