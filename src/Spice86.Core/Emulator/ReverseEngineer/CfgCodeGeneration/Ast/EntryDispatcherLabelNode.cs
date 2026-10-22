namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>The <c>entrydispatcher</c> label cyclic jumps re-enter; printed only when such a jump exists.</summary>
internal sealed record EntryDispatcherLabelNode : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitEntryDispatcherLabelNode(this);
}
