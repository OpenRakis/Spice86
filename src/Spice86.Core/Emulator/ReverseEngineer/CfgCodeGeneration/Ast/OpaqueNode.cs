namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>A statement shape the lifter does not know; its effects are <see cref="Ir.IrEffects.Unknown"/>.</summary>
/// <param name="Original">The wrapped statement.</param>
internal sealed record OpaqueNode(IVisitableAstNode Original) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitOpaqueNode(this);
}
