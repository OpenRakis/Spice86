namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>A <c>VerifySpeculativeEntryOrFail</c> guard re-checking a speculative instruction's bytes before its body runs.</summary>
/// <param name="Address">The address of the guarded speculative instruction.</param>
/// <param name="SignatureField">The name of the generated field holding the expected signature bytes.</param>
internal sealed record SpeculativeGuardNode(SegmentedAddress Address, string SignatureField) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitSpeculativeGuardNode(this);
}
