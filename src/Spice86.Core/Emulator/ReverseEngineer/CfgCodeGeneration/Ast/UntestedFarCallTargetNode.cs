namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>A delegate that fails if a selector-driven far call unexpectedly takes its direct path.</summary>
/// <param name="CallAddress">The far call whose literal selector has no observed direct target.</param>
internal sealed record UntestedFarCallTargetNode(SegmentedAddress CallAddress) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) =>
        CodeGenerationNode.RequireVisitor(astVisitor, this).VisitUntestedFarCallTargetNode(this);
}
