namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>A <c>return InterruptRet();</c> transfer out of a generated method. An interrupt return restores
/// FLAGS from the stack, so its effect on the method's values differs from an ordinary exit.</summary>
/// <param name="Width">The operand width of the interrupt return: 32 bits for <c>iretd</c>.</param>
internal sealed record InterruptReturnNode(BitWidth Width) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitInterruptReturnNode(this);
}
