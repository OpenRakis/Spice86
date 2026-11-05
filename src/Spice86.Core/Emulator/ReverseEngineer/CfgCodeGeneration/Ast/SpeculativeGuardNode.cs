namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>A speculative guard re-checking the bytes of a straight-line run of speculative
/// instructions before its first body runs. Printed as <c>VerifySpeculativeEntryOrFail</c> for a
/// single instruction and <c>VerifySpeculativeRunOrFail</c> for a merged run.</summary>
/// <param name="Address">The address of the run's first speculative instruction.</param>
/// <param name="SignatureFields">The names of the generated fields holding the expected signature
/// bytes of the run's instructions, in memory order.</param>
internal sealed record SpeculativeGuardNode(SegmentedAddress Address, IReadOnlyList<string> SignatureFields) : IVisitableAstNode {
    /// <summary>The name of the generated <c>byte?[][]</c> field holding the run's signatures,
    /// assigned once per distinct merged run when the signature fields are emitted.</summary>
    public string? RunFieldName { get; init; }

    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitSpeculativeGuardNode(this);
}
